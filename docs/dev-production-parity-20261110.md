# Site_2024 | DEV and production feature parity (10 Nov 2026)

## Scope
Port production **application behavior** to DEV, without copying production store, SQL,
credentials, API destinations, or deployment configuration. These changes must be
reviewed and tested on DEV before the parity branches are merged. No Shopify write
operations or database migrations were executed as part of this change.

### Preserved DEV-only functionality
- `ShopifyDevStoreIdentityController`: read-only store-identity verification.
- `ShopifyDevLegacyRehearsalController`: tightly gated DEV-only shipping rehearsal.
- `ReferenceDeletionController`, policy and service.
- Existing DEV-safe protection: a Shopify-linked part cannot be deleted locally only.
- DEV-only tests, workflows and SQL migrations.

### Production behavior ported into DEV
- Market-driven shipping collection verification for new parts and publishing.
- Pending-to-Available only after confirmed Online Store publication.
- Latest shipping membership audits and shipping reassignment queue/worker safeguards.
- Shipping policy and tag management endpoints and supporting models/services.
- Frontend parity is in a companion React branch; it includes the previously requested
  *one best-effort publish attempt then navigate* behavior rather than three retries.

## Environment isolation: MUST verify before deploying

**Frontend:** Keep `REACT_APP_API_HOST_PREFIX` pointing to the isolated DEV API,
not `https://api.grsonsautomotive.com`. The DEV Azure Static Web App workflow must
still target the DEV deployment (not the production workflow).

**API:** `ShopifySettings__ShopDomain` must equal exactly
`gr-sons-shipping-dev.myshopify.com`, and the Shopify credentials, publication
GID and location GID must belong to that DEV store. Never copy production
`ClientId`, `ClientSecret`, store-specific IDs, or other settings into DEV.
The checked-in Development fallback has credentials/location IDs blanked; supply
DEV-only secrets and IDs through secure Azure App Service settings.

**Fail closed:** `ShopifyTokenService` refuses to obtain or return any access token
unless the configured shop is the isolated DEV shop. `ShopifyCheckoutService`
refuses to generate checkout URLs for any other store. These DEV-specific
defenses are deliberately not part of the production branch.

**Database:** The DEV API connection string must point to the `Site_2024_Dev`
database. Check the server name as well as the database name. Never execute the
production-only `Site_2024` migrations against DEV or vice versa.

## SQL and Shopify readiness checks

1. Run `database/migrations/20261110_00_DEV_Parity_Preflight_READ_ONLY.sql`
   manually **only against Site_2024_Dev**. Review whether the shipping
   collection mapping column and durable queue schema/indexes exist.
2. If DEV is missing the queue, review the already-existing DEV migration
   `database/migrations/20261026_01_ShopifyShippingReassignmentJobs_DEV.sql`
   before applying it. Do not automatically enable async worker feature flags.
3. Verify relevant shipping policies have DEV-store collection mappings.
   A valid production collection GID is not valid for DEV.
4. With AdminHigh authorization, check the DEV-only
   `GET /api/shipping-migration/legacy/shop-identity` response and require
   `isolatedDevStoreVerified = true`.
5. Keep mutation/rehearsal gates off unless explicitly testing the isolated
   DEV store with disposable test products.

## Manual acceptance tests
- DEV frontend calls DEV API; the production API does not receive requests.
- New checkout part with a test shipping tier and photos remains hidden
  until Shopify confirms publication. One publish request is made; the admin
  page is navigable without waiting for publishing retries.
- Pending Publishing review and manual Publish Selected use the DEV store
  and do not publish contact-only or zero-stock items.
- Shopify collection-membership 409 leaves the listing Pending; manual
  retry can be performed from Pending Publishing after verification.
- Existing DEV reference creation/deletion, shipping audit and Shopify-only
  rehearsal routes still work with their own authorization gates.
- Existing DEV/production shipping rate profiles and product inventories are
  left untouched by the parity preparation itself.

## Release gate
Build-only CI passing is necessary but not sufficient. Do **not** merge the
parity branches into `dev` before checking the live Azure App Service
Shopify environment, the DEV database preflight, and the manual Shopify
identity result. A GitHub branch check cannot read those Azure settings.
