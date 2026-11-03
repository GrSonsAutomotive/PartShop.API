# Production Shipping Reassignment Queue — Release Checklist

The existing queue, worker, and authorization settings are already present on API master.
New-part automatic publishing is already working in production; do not alter it.

## Review and deployment
1. Review and merge this API release after its build passes. Keep all reassignment flags OFF.
2. Verify production store identity with the existing read-only endpoint:
   `GET /api/shipping-migration/legacy/shop-identity`.
3. Run `database/migrations/20261102_00_ProductionShippingQueue_Preflight_READ_ONLY.sql`
   manually against production `Site_2024`. Save the results and check Available
   versus Unavailable statuses, collection mappings, duplicates and existing queue.
4. If the queue table is absent, run the already-existing PRODUCTION-only
   `20261028_02_ProductionShippingJobsSchema.sql` after DBA review.
   Do not drop or reset a table that is present.
5. Read-only check automatic-source part 3091 and a legacy-selected part 611
   through `GET /api/home/{id}/shopify/shipping-reassignment-plan?targetShippingPolicyId=N`.
   Expect complete scans, no legacy hits on the new part, and legacy hits on
   the older part. No writes should be performed.
6. Review frontend queue UI PR #2 against current frontend main (which includes
   automatic new-part publishing). Do not merge read-only UI PR #4 instead.
7. Stage configuration ONLY after checking the actual authenticated Shopify
   store domain and the queue schema:
   `ShopifySettings__AsyncShippingJobsSchemaReady=true`
   `ShopifySettings__AsyncShippingAuthorizedShopDomain=<verified production shop>`
   `ShopifySettings__AllowProductionAsyncShipping=true`
   Keep the next two flags FALSE until the controlled pilot:
   `ShopifySettings__EnableMarketDrivenShippingAsyncWorker=false`
   `ShopifySettings__AllowMarketDrivenShippingAsyncReassignmentWrites=false`
8. At an approved quiet-window pilot, enable the worker and then queue writes.
   Use ONE auto-only part and verify job status, collection membership, SQL
   availability and checkout rate after completion.

## Scope and failures
- The first rollout supports only existing products with NO legacy manual
  VARIANTS selection in ANY mapped active checkout shipping collection.
- Product-specific full membership scanning occurs before queue insertion
  and again inside the worker. Unknown or truncated data fails closed.
- Legacy-selected products must not be queued; do not delete old sources.
- Queue writes can be halted with the queue-write flag. Do not stop a worker
  mid-job without checking Shopify and SQL; the part may be unpublished.
- Never blindly retry an incomplete job or manually overwrite SQL policy.
- No production Shopify or SQL writes were performed by preparing this PR.
