# GR Sons Automotive — Production Shipping Cutover (Preparation)

**Status: PREP ONLY. No production deployments, SQL execution, or Shopify upgrade has been performed from this branch.**

## Scope and source of truth

- Production DB: `Site_2024`; DEV DB: `Site_2024_Dev`.
- Existing production inventory baseline from SQL: 3,064 parts; 3,063 Shopify-linked; 1 local-only.
- Checkout-enabled shipping policy IDs 9–19 (11 groups); contact-only policy 20 (16 parts).
- Existing delivery profiles and Shopify product IDs MUST remain intact until Shopify's supported migration.
- Production Shopify store identity verified from Azure App Service settings: `bx7ns0-2i.myshopify.com`. DEV is `gr-sons-shipping-dev.myshopify.com`; do not confuse them.

## Changes packaged here

- `20261028_00_ProductionShipping_Baseline_READ_ONLY.sql`: inspect production without writes.
- `20261028_01_ProductionShippingCollectionMapping.sql`: nullable collection mapping + select proc. Never alters original profile IDs.
- `20261028_02_ProductionShippingJobsSchema.sql`: empty durable queue and indexes; no part updates.
- Application settings `ShopifySettings__AllowProductionAsyncShipping=false` and `ShopifySettings__AsyncShippingAuthorizedShopDomain` form separate explicit shop authorization. DEV is the only allowed shop by default.

## Mandatory sequence (stop if any check fails)

1. Export SQL baseline, duplicate Shopify ID report, and a recoverable Azure SQL backup/export; verify restore plan. Keep this record.
2. Inventory all current production US/International Markets, 11 custom shipping profiles, location settings, shipping rates and combinations; verify actual test checkouts before upgrade.
3. Production shop domain verified as `bx7ns0-2i.myshopify.com`. During cutover, independently reconfirm Azure `ShopifySettings__ShopDomain` matches it. Ensure `REACT_APP_API_HOST_PREFIX=https://api.grsonsautomotive.com` on the release frontend. Do not copy DEV `.env`.
4. Complete API code review, compile/test in an isolated non-production pipeline, and remove hard-coded DEV one-off recovery endpoints before merging.
5. Before applying SQL changes, confirm `ShopifySettings__AsyncShippingJobsSchemaReady=false`, `ShopifySettings__EnableMarketDrivenShippingAsyncWorker=false`, `ShopifySettings__AllowMarketDrivenShippingAsyncReassignmentWrites=false`, `ShopifySettings__AllowMarketDrivenShippingReassignmentWrites=false`, `ShopifySettings__AllowMarketDrivenShippingRecoveryWrites=false`, `ShopifySettings__AllowMarketDrivenShippingCollectionMembershipWrites=false`, and `ShopifySettings__AllowMarketDrivenShippingCollectionSourceWrites=false`. `AllowProductionAsyncShipping=false` remains false through migration.
6. Run schema migrations individually in SSMS, only after reviewing the baseline and exact database guards. These do not modify parts or Shopify.
7. Schedule Shopify `Upgrade` with the client during a controlled window (not before the API release is safe). Shopify is responsible for migrating existing delivery-profile groups/rates into Market-driven Shipping; **audit rather than assume success**.
8. Inspect Shopify-created `[SHIPPING]` collections and market rates; do not create duplicates. Map each of the 11 local policies to the correct migrated collection, leaving policy 20 contact-only.
9. Add verified tag-source conditions. Do not remove migrated manual selections until an independent audit confirms no product is lost. Current membership removal endpoints have strict limits; large groups need a reviewed paginated migration mechanism.
10. Use the **new staged, tags-only, PartId-cursor** batch routine after its build and DEV validation. It adds missing ShippingClass tags without replacing other Shopify tags or product content. Do NOT run the existing `POST /api/home/shopify/tags/backfill`: it also rewrites Shopify titles and descriptions.
11. Independently reconcile each product and each shipping group; validate all active, draft and contact-only cases, duplicates, availability, images, inventory, and sample checkout rates for both single-item and mixed-profile carts.
12. Complete deterministic restart/failure recovery tests, then enable features gradually using one carefully selected product. Only after approval set the verified production shop domain and its independent production authorization flag.
13. Monitor job completion, unavailable listings and checkout charges; keep explicit rollback/incident recovery steps.

## Pending blockers

- Production Shopify store identity is verified; Shopify upgrade contents and post-upgrade shipping rates/membership are **not** verified.
- Tags-only backfill staged with separate disabled-by-default authorization; requires build, DEV verification, and limited production canary. Migrated manual-selection handling for large collections remains unverified.
- Production-suitable worker restart/failure testing still pending.
- Existing DEV-only recovery path needs removal/review before code merge.

**Schema migrations 01/02 were run and verified in production after the safety copy. Do not flip the Shopify Upgrade control, enable write flags, or merge to production until the remaining preflight succeeds.**

## Observed production checkout baseline (before Shopify Upgrade)

Captured from three-item customer checkout screenshots on 2026-10-27 (America/Los_Angeles).
These screenshots verify rates for the tested cart only; they do not establish all
shipping zones or map each product to a specific policy ID.

| Cart / selected methods | Shipping total | Order total |
| --- | ---: | ---: |
| Three items, Standard free shipping on each shipment | $0.00 | $397.85 |
| Three items, paid Economy method on each shipment | $85.00 | $482.85 |

Items and paid Economy rates visible:
- NEW Porsche 968 Transmission Shim .50mm — $15.00.
- Porsche 924 / 944 Center Dash AC Air Vent Outlet — $15.00.
- Porsche 912 Engine Suspension Mounting Bracket — $55.00.
- Shipment 2 also showed an Express $50.00 option.

**Verification after the upgrade:** repeat with exactly these three products,
quantities, destination/ZIP, shipping methods and market. The shipping baseline is
not comparable if the address, product policy, or rate selection changes.
Do not place a paid order. Save separate rate snapshots for each of the 11 policy groups.
