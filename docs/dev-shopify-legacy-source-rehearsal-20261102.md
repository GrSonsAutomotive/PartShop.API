# DEV-only Shopify legacy variant source rehearsal (2026-11-02)

This document tests only **gr-sons-shipping-dev.myshopify.com**, using the throwaway collection:
- Collection: `gid://shopify/Collection/511239127260` — `[SHIPPING] Site_2024 - MIGRATION TEST`
- Automatic rule source: `gid://shopify/CollectionConditionsSource/54605054172` — `ShippingClass_21`
- Expected existing collection membership: **1 product, 1 variant**.
- The 11 normal DEV shipping collections and ALL production collections are out of scope.

## Hard safety gates (in code)

All endpoints are AdminHigh/PartsDelete. GraphQL can only reach the literal development store above and only from the specific Azure DEV API hostname. The authenticated shop identity, fixed collection GID/title, fixed automatic source GID and condition, one Shopify product, one Shopify variant, product's tag, and source/member topology are re-verified on every operation.

Write operations are **off** by default. For a short DEV rehearsal, explicitly enable only
`ShopifySettings__AllowDevLegacySourceRehearsal=true` **on the DEV Azure App Service**.
Disable it immediately after rehearsal. This flag has no use on production.

No endpoint accepts an arbitrary Shopify collection, shop, product or variant. All three mutations have distinct confirmation strings and expected read-only IDs. Never modify production Shopify settings or credentials.

## Steps

1. Merge the DEV-only feature PR into `dev` (not `master`) and confirm the DEV Azure deployment succeeded.
2. Read-only GET `/api/shipping-migration/dev-rehearsal/preflight`. Must show the development Shopify store, product/variant GIDs, the exact automatic source GID, `sourceCount=1`, `manualSourceGid=null`, `devRehearsalWritesEnabled=false`. Save the response.
3. In DEV Azure App Service only, enable the flag above. Expect the App Service to restart.
4. Read-only GET preflight again. Verify `devRehearsalWritesEnabled=true`, all GIDs identical, source count 1.
5. POST `/api/shipping-migration/dev-rehearsal/create` with JSON:

```json
{
  "confirmation": "DEV_CREATE_TEMP_VARIANT_SOURCE",
  "expectedProductGid": "COPY_FROM_PREFLIGHT",
  "expectedVariantGid": "COPY_FROM_PREFLIGHT",
  "expectedAutoSourceGid": "gid://shopify/CollectionConditionsSource/54605054172"
}
```

6. Save the new `currentManualSourceGid` and return to GET preflight. Expect exactly two sources (automatic + the temporary variant source), same single product/variant, and settled source membership. The UI may need another read while asynchronous Shopify jobs settle. Never re-POST on a timeout without inspecting preflight.
7. POST `/api/shipping-migration/dev-rehearsal/delete`:

```json
{
  "confirmation": "DEV_DELETE_TEMP_VARIANT_SOURCE",
  "expectedProductGid": "COPY_FROM_PREFLIGHT",
  "expectedVariantGid": "COPY_FROM_PREFLIGHT",
  "expectedAutoSourceGid": "gid://shopify/CollectionConditionsSource/54605054172",
  "expectedManualSourceGid": "COPY_FROM_TEMP_SOURCE"
}
```

8. Save the removal response, then GET preflight: the automatic source alone must preserve the product/variant and full collection membership.
9. POST `/api/shipping-migration/dev-rehearsal/restore` with the same fixed product/variant/auto GIDs and confirmation `DEV_RESTORE_TEMP_VARIANT_SOURCE`. Save the **new manual source GID** and GET preflight again. It should have two sources, explicit selection of exactly the original variant, and unchanged one-product final membership.
10. Verify DEV checkout shipping rates before/after source deletion and restoration if the test store has applicable Markets shipping. The GraphQL membership test alone doesn't validate price presentation.
11. Set `ShopifySettings__AllowDevLegacySourceRehearsal=false` on DEV and record the final preflight. Keep production flags disabled.

## Failure rules

- `409` from preflight means no mutation was made; fix the DEV source shape or let Shopify settle.
- A `502 outcomeUncertain` after POST means Shopify may have accepted the mutation. **Do not repeat it blindly**. Inspect the read-only preflight, compare source GIDs, and reconstruct from the saved snapshot.
- If source deletion changes any product membership or checkout rate, STOP. Do not proceed to production.
- The source GID **changes** after restoration; production recovery must never rely on a stable source GID.

## Production reminder

This feature PR is DEV-only. It must not be merged to `master`. Production cleanup will use a separately reviewed, guarded implementation after the DEV rehearsal has passed.
