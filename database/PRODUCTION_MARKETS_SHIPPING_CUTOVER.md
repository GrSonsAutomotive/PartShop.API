# GR Sons Automotive — Production Shipping Cutover (Preparation)

**Status: PREP ONLY. No production deployments, SQL execution, or Shopify upgrade has been performed from this branch.**

## Scope and source of truth

- Production DB: `Site_2024`; DEV DB: `Site_2024_Dev`.
- Existing production inventory baseline from SQL: 3,064 parts; 3,063 Shopify-linked; 1 local-only.
- Checkout-enabled shipping policy IDs 9–19 (11 groups); contact-only policy 20 (16 parts).
- Existing delivery profiles and Shopify product IDs MUST remain intact until Shopify's supported migration.
- Production myshopify.com domain has **not been independently verified**. Do not guess it or enable jobs.

## Changes packaged here

- `20261028_00_ProductionShipping_Baseline_READ_ONLY.sql`: inspect production without writes.
- `20261028_01_ProductionShippingCollectionMapping.sql`: nullable collection mapping + select proc. Never alters original profile IDs.
- `20261028_02_ProductionShippingJobsSchema.sql`: empty durable queue and indexes; no part updates.
- Application settings `ShopifySettings__AllowProductionAsyncShipping=false` and `ShopifySettings__AsyncShippingAuthorizedShopDomain` form separate explicit shop authorization. DEV is the only allowed shop by default.

## Mandatory sequence (stop if any check fails)

1. Export SQL baseline, duplicate Shopify ID report, and a recoverable Azure SQL backup/export; verify restore plan. Keep this record.
2. Inventory all current production US/International Markets, 11 custom shipping profiles, location settings, shipping rates and combinations; verify actual test checkouts before upgrade.
3. Confirm the genuine production `*.myshopify.com` domain and environment settings. Ensure `REACT_APP_API_HOST_PREFIX=https://api.grsonsautomotive.com` on the release frontend. Do not copy DEV .env.
4. Complete API code review, compile/test in an isolated non-production pipeline, and remove hard-coded DEV one-off recovery endpoints before merging.
5. Before applying SQL changes, confirm `ShopifySettings__AsyncShippingJobsSchemaReady=false`, `ShopifySettings__EnableMarketDrivenShippingAsyncWorker=false`, `ShopifySettings__AllowMarketDrivenShippingAsyncReassignmentWrites=false`, `ShopifySettings__AllowMarketDrivenShippingReassignmentWrites=false`, `ShopifySettings__AllowMarketDrivenShippingRecoveryWrites=false`, `ShopifySettings__AllowMarketDrivenShippingCollectionMembershipWrites=false`, and `ShopifySettings__AllowMarketDrivenShippingCollectionSourceWrites=false`. `AllowProductionAsyncShipping=false` remains false through migration.
6. Run schema migrations individually in SSMS, only after reviewing the baseline and exact database guards. These do not modify parts or Shopify.
7. Schedule Shopify `Upgrade` with the client during a controlled window (not before the API release is safe). Shopify is responsible for migrating existing delivery-profile groups/rates into Market-driven Shipping; **audit rather than assume success**.
8. Inspect Shopify-created `[SHIPPING]` collections and market rates; do not create duplicates. Map each of the 11 local policies to the correct migrated collection, leaving policy 20 contact-only.
9. Add verified tag-source conditions. Do not remove migrated manual selections until an independent audit confirms no product is lost. Current membership removal endpoints have strict limits; large groups need a reviewed paginated migration mechanism.
10. Use a **new tags-only, resumable** bulk-sync routine (to be implemented) for linked products. Do NOT run the existing `POST /api/home/shopify/tags/backfill`: it also rewrites Shopify titles and descriptions.
11. Independently reconcile each product and each shipping group; validate all active, draft and contact-only cases, duplicates, availability, images, inventory, and sample checkout rates for both single-item and mixed-profile carts.
12. Complete deterministic restart/failure recovery tests, then enable features gradually using one carefully selected product. Only after approval set the verified production shop domain and its independent production authorization flag.
13. Monitor job completion, unavailable listings and checkout charges; keep explicit rollback/incident recovery steps.

## Pending blockers

- Production Shopify store identity and Shopify upgrade contents not yet verified.
- Bulk tags-only routine and >10-item collection membership handoff not production-ready.
- Production-suitable worker restart/failure testing still pending.
- Existing DEV-only recovery path needs removal/review before code merge.

**Do not flip the Shopify Upgrade control, run the SQL scripts or merge to production until the above preflight succeeds.**
