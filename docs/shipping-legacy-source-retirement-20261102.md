# Shopify legacy shipping-source retirement — staged runbook

Last revised: 2026-11-02. **No production source deletion is authorized by this document.**

## Why the readiness audit changed

The 2026-10-28 baseline reconciled 3,047 checkout-linked SQL products in 11 Shopify shipping collections. At that instant each collection had two equivalent sources: the migrated legacy `VARIANTS` selection source and the new `PRODUCTS` automatic `ShippingClass_<policy ID>` tag source.

The site now creates new inventory after the migration. New products join the **automatic** source and are never assigned to the migrated manual variant source. Therefore, requiring all four sources to equal current SQL would falsely fail legitimate product additions.

**Correct invariant per policy:**

1. `SQL-linked current product GIDs == automatic tag source product GIDs == entire collection product GIDs`.
2. `legacy source product GIDs == legacy explicitly selected product GIDs`, and this historic set is a **subset** of current SQL.
3. Every historic explicit variant selected by the old source is *exactly* the sole SQL-linked variant of that product, and every current SQL product has exactly its expected sole variant in Shopify.
4. Exactly two recognized sources and no exclusions or additional conditions; verify snapshots before and after.
5. Any discrepancy, missing mapping, unstable cursor, or unsupported shape fails closed.

The `GET /api/shipping-migration/legacy/retirement-plan/{policyId}` endpoint is **read-only**, requires AdminHigh/PartsDelete, returns `ready` plus `problems` and identifies the historic member subset as `legacyRecoverySelections`.

## Capture a recovery manifest BEFORE any delete

For each target policy, save the entire JSON response with its Shopify collection GID, **original source GID and title**, and explicit product/variant pairs. The old GID cannot be assumed restorable after deletion; recreating produces a new source GID. The `legacyRecoverySelections` subset contains only historic original selections; never include new auto-only products in the restored manual source.

A `ready: true` result is a snapshot, not proof that Shopify can recreate the source, or that checkout shipping rules are unchanged.

## CRITICAL: Verify DEV Shopify isolation

The repository's `Site_2024/appsettings.Development.json` currently names
`bx7ns0-2i.myshopify.com` (the PRODUCTION shop). Azure environment variables
may override this, but the file alone is NOT evidence of a separate DEV store.
**Never attempt a DEV deletion or restoration until the runtime SHOPIFY identity
is verified as an isolated test shop.**

New read-only endpoint:
`GET /api/shipping-migration/legacy/shop-identity` (AdminHigh).

On the DEV API, it must return `isolatedDevStoreVerified: true`, with BOTH
`configuredShopDomain` and `actualShopDomain` equal to
`gr-sons-shipping-dev.myshopify.com`. If it returns false or fails,
**STOP**. Do not enable collection membership or source write flags.

This check queries the actual Shopify store authenticated by the API token.
It never writes to Shopify or SQL.

## DEV rehearsal only — no production writes

Use `gr-sons-shipping-dev.myshopify.com` with a throwaway DEV collection containing:
- A variant-targeted manual source with explicit selected variant IDs;
- An automatic tag source matching the same DEV test products;
- If possible, at least one newly-added auto-only product not in the manual source.

Confirm baseline checkout shipping behavior first. Then, while recording source and product IDs:

1. Query all Shopify source selections (including `variantIds`) and verify the complete manifest.
2. Use the documented Shopify 2026-07 `collectionUpdate` mutation with **only** `sourcesToDelete: [<DEV manual source GID>]`. No SQL or production writes. Review `userErrors` and any returned `job` (membership updates may be asynchronous).
3. Poll for Shopify membership settlement. Confirm the collection has *only* the intended automatic source, the full expected product set, and unchanged DEV checkout rates. Do not infer success from a single `job.done` or product count.
4. Restore the original source in DEV using `collectionUpdate` with `sourcesToCreate`, `source.targetType: VARIANTS` and `source.inclusion.selections` entries with `productId` and exact `variantIds`. This recreates the source with a **new GID**; preserve source title and test all product / variant selections. For large cohorts, assess Shopify mutation payload limits and chunking in DEV before designing production recovery.
5. Confirm full settled collection and source membership; rerun DEV checkout rates. Record failure/recovery timing and any API errors.

Official reference:
- https://shopify.dev/docs/api/admin-graphql/2026-07/mutations/collectionUpdate
- https://shopify.dev/docs/api/admin-graphql/2026-07/input-objects/CollectionUpdateInput
- https://shopify.dev/docs/api/admin-graphql/2026-07/mutations/collectionCreate (variant selection example)

**Illustrative DEV-only restore mutation** (fill ONLY with a test-store collection/product/variant pair):

```graphql
mutation RestoreDevSource($collection: CollectionUpdateInput!) {
  collectionUpdate(collection: $collection) {
    collection { id sources { id title } }
    job { id done }
    userErrors { field message }
  }
}
```

```json
{
  "collection": {
    "id": "gid://shopify/Collection/DEV_COLLECTION_ID",
    "sourcesToCreate": [{
      "source": {
        "title": "DEV original variant source",
        "targetType": "VARIANTS",
        "inclusion": {
          "selections": [{
            "productId": "gid://shopify/Product/DEV_PRODUCT_ID",
            "variantIds": ["gid://shopify/ProductVariant/DEV_VARIANT_ID"]
          }]
        }
      }
    }]
  }
}
```

## Production only after DEV rehearsal & approval

- Freeze **shipping policy edits, publication and adds** briefly for the target collection. Ordinary browsing may continue, subject to controlled checkout smoke tests.
- Re-run read-only preflight immediately before each mutation; export recovery manifest to an independent location.
- Start with the smallest verified policy (19, five historic products). Delete exactly one **specified** legacy source, never the entire shipping collection.
- Watch Shopify job settlement; compare automatic and collection member **IDs**, and verify exact checkout rates for representative carts.
- Stop on first mismatch, rollback following DEV-tested source re-creation, and escalate if checkout rates fail.
- Only then plan remaining 10 policies, with rate/volume-aware per-collection controls.
- Do not enable the production async reassignment worker, shipping-policy membership writes, or old-source deletion flags merely to run an audit.

## Current authorized change

This branch updates only the read-only membership audit to recognize newly-added auto-only inventory and produce a historic recovery subset. **It adds no Shopify mutations and deploys no feature flags.**
