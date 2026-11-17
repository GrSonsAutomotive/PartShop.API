# Shopify-linked Inventory Cleanup — DEV Pilot

**Do not run this on production.** This feature extends the earlier Inventory Cleanup work.
Only unsold, old, unavailable **DRAFT** products belonging to the connected,
explicitly authorized Shopify test store can be permanently deleted. It never
deletes a product whose `Site_2024` and `SitePartId_<id>` tags do not match.

## Run order (DEV only)

1. Confirm the original `20261117_InventoryCleanup_DEV.sql` migration has been
   installed in the **isolated DEV SQL database**.
2. Review and apply
   `Site_2024/SqlMigrations/20261117_InventoryCleanup_Shopify_DEV.sql`.
   This SQL script does not contact Shopify or delete any data.
3. Inspect the actual connected Shopify identity with the existing read-only
   `GET /api/shipping-migration/legacy/shop-identity` endpoint.
4. Ensure the **API's actual configured Shopify shop and token** point to
   `gr-sons-shipping-dev.myshopify.com`. Some historic DEV appsettings files
   reference different stores. The API gate refuses those stores by default.
5. In isolated DEV Azure API settings only, set:
   - `ShopifySettings__InventoryCleanupAuthorizedShopDomain=gr-sons-shipping-dev.myshopify.com`
   - `ShopifySettings__AllowInventoryCleanupShopifyDeleteWrites=true`
   - `ShopifySettings__AllowProductionInventoryCleanupShopifyDeletes=false`
6. Deploy the DEV API branch, then the frontend branch, or use PR preview with
   a **DEV API URL**. Do not connect a preview React app to the production API.
7. With a throwaway **unsold** test part (age adjusted in isolated test data,
   not by weakening production minimum), test preflight → confirm ID → Shopify
   DRAFT productDelete → SQL finalization → queued photo removal.
8. Remove the opt-in flag if an unexpected result occurs. Review
   `dbo.InventoryCleanupShopifyOperations` before any manual recovery.

## Recovery rules

- Shopify product ID and the **authenticated** `shop.myshopifyDomain` are
  independently checked against the authorized store before any mutation.
- SQL journal persists `Prepared`, `Attempted`, `ShopifyDeleted`,
  `Completed`, and `Cancelled`. Each part's mutation sequence is serialized
  using a SQL session application lock.
- The code records **Attempted before the network mutation**. A timeout or
  ambiguous response leaves the part locally intact; retry reads Shopify again.
- A completed Shopify deletion is only followed by local deletion if all SQL
  eligibility checks **still pass**, with photo URLs queued transactionally.
- If local finalization fails, the journal remains `ShopifyDeleted`; a subsequent
  explicit admin retry finishes the SQL operation without issuing another
  Shopify productDelete.
- Rows marked `Prepared` can be cancelled before any external mutation. Any
  `Attempted` row is intentionally locked until reconciliation.
- Recorded **sales, refunds, discount references, and active shipping jobs**
  block deletion. Do not override their guards.
- Only an AdminHigh authenticated user with correct typed ID may invoke delete.

## Manual validation cases (DEV)

- Opt-in flag false: preview blocked; no Shopify write
- Wrong configured store / authenticated store mismatch: preview blocked
- Status ACTIVE: blocked, instruct unpublish via existing admin workflow
- Missing ownership tag / mismatched ID: blocked without mutation
- Valid DRAFT, no sales, >=90 days unavailable: succeeds and image cleanup runs
- Existing Shopify product absent: no mutation; journal reconciles the missing product
- Simulated mutation timeout: `Attempted` persisted; local listing stays; retry reads Shopify first
- Simulated SQL finalize failure: `ShopifyDeleted` persists; retry finalizes without deleting Shopify again
- Two concurrent confirmations: only one operation at a time for the same part
- Sold/refunded part, discount-linked part, pending shipping job, new/available part: blocked
- Check photo cleanup queue and audit archive after success
- Verify AdminLow 403 and frontend hides this feature

## Production rollout NOT included

A separate approval/release must consider archive-only handling for parts with
sales/returns, access-token store identity, any Shopify order history not
represented locally, and safe activation in the real Shopify store. All
new feature flags default to false.
