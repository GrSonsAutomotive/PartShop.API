# Inventory Cleanup (DEV-first)

Feature branches: `feat/inventory-cleanup-20261117` on both API and React repos.

## Deployment order

1. Back up the DEV Azure SQL database and review `Site_2024/SqlMigrations/20261117_InventoryCleanup_DEV.sql`.
2. Execute that migration in DEV Azure SQL only. It creates `Parts.UnavailableSinceUtc`, a status-transition trigger, an audit archive, a photo purge queue, and `InventoryCleanup_Delete`.
3. Merge/deploy the **API DEV** branch after the migration. Verify `GET /api/admin/inventory-cleanup` as AdminHigh.
4. Merge/deploy the **React DEV** branch. Browse Admin Actions > Inventory Cleanup.
5. Run the tests below before any production migration.

**Important:** Neither a GitHub commit nor these migration files executes SQL. Running the migration is a separate administrator action.

## Business rules

- Only AdminHigh can access the cleanup API (existing `PartsDelete` policy).
- The search filter offers 30/60/90/180/365 days and sorts **oldest `Parts.DateModified` first**.
- The mandatory **deletion minimum is 90 days since transition into Unavailable**, rechecked under a SQL row lock; changing the search filter never weakens it.
- Existing unavailable rows are initialized from `Parts.DateModified` as a fallback; this is an estimate because historical status transitions cannot reliably be reconstructed from this column alone.
- Sale/return-linked parts cannot be permanently deleted, because historical financial records must be preserved.
- Shopify-linked parts are currently **blocked**, not locally deleted. A separate resumable Shopify product-deletion/archive workflow is required before enabling them.
- Discounts referencing the part also block deletion.
- The confirmation body must include the exact Part ID, not just a frontend click.
- SQL stores a snapshot and original edit-history rows, removes the unsold/unlinked part in a transaction, and queues only unshared image URLs.
- Photo cleanup only deletes files inside the configured `UploadStorage:RootPath/items` and only matching app-generated GUID filenames. Failures are retained in the SQL queue; AdminHigh can retry them.
- Legacy `DELETE /api/home/{id}` returns 410 Gone to prevent bypass.

## DEV smoke-test checklist

- AdminLow gets 403 and cannot see the Admin Actions cleanup tile.
- AdminHigh can search unavailable parts and selects one without leaving the page.
- Detail card is initially blank and selecting another row refreshes its image, metadata, and eligibility.
- "View Full Details" opens `/admin/part/{id}` in a separate tab.
- Eligible unsold, Shopify-unlinked, unavailable >=90-day part requires typing exact Part ID; deletion removes the part, preserves its archived edit history, and queues all unshared photos.
- Part unavailable for <90 days is blocked regardless of selected search threshold.
- Available/Pending part is blocked even if old.
- Sold, refunded, discounted, Shopify-linked, or pending-shipping-job part is blocked.
- Retry photo queue handles already-absent files idempotently and does not touch paths outside uploads/items.
- SQL and Shopify records are inspected before/after testing. Do not use production records for tests.

## API contract

- `GET /api/admin/inventory-cleanup?olderThanDays=90&query=&pageIndex=0&pageSize=20`
- `GET /api/admin/inventory-cleanup/{partId}/preview`
- `DELETE /api/admin/inventory-cleanup/{partId}` with body `{"confirmPartId":123}`
- `POST /api/admin/inventory-cleanup/photos/retry`

## Open follow-up

A coordinated Shopify delete/archival workflow is needed for normal published inventory. Do **not** bypass the Shopify link check or the sale/return-history check to make deletion succeed. For sold inventory, an archive workflow that frees listing photos while preserving the part row and transaction ledger is preferable.
