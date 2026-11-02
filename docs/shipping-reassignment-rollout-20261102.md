# Shipping-policy reassignment rollout — scope and safety

## Existing production status (2026-11-02)

- New checkout-enabled parts automatically publish after gallery upload, tag sync, collection verification, and inventory checks. Verified against production part 3091, policy 10 / `ShippingClass_10`.
- Original migrated Shopify `VARIANTS` sources remain in the 11 production shipping collections. They are NOT required for new-part automatic publishing and must NOT be deleted as a prerequisite.
- Ordinary part PATCH rejects shipping-policy changes for Shopify Markets listings. The sync and async write paths are not deployed/enabled in production.
- Older products may remain in a former shipping collection even after changing tags because an original explicit legacy variant selection still includes them.

## What this branch changes

Only the existing protected **read-only** `GET /api/home/{partId}/shopify/shipping-reassignment-plan?targetShippingPolicyId=N` audit.
It now pages every legacy explicit variant-selection source across **all mapped active checkout policies** (up to 50 pages × 100 selections per source). The previous preview used Shopify's sampled first ten and could not reliably prove a part lacked legacy assignments.

Readiness conditions to inspect before a later production write feature:
- `allMappedManualSelectionScansComplete` is true;
- `collectionsWithIncompleteScans` and `unmappedActiveCheckoutPolicyIds` are empty;
- `manualMembershipsAcrossMappedCollections` is empty for a **new** tag-only part, or a separate targeted legacy cleanup/recovery flow has been developed;
- current tag matches SQL; destination has correct automatic ShippingClass source;
- source/target Shopify membership, exact product and publication state will need revalidation at **execution time**.

The preview is NOT a reservation, a safe-to-write token, or an authorization to change the policy. Fail closed if source pagination is incomplete or any topology is unknown.

## Suggested read-only production validation after merge

1. Preview auto-only part **3091** (current policy 10, target another active checkout tier such as policy 11). Expect no explicit manual membership, complete source scans and correct current tag; no writes.
2. Preview legacy part **611** (policy 19, target policy 10). Expect it to be flagged with a historic manual membership in policy 19. This confirms we are *not* silently overriding a legacy assignment.
3. Confirm existing part's SQL shipping policy and Shopify memberships are unchanged by previews.

## Future rollout, separate branches

1. DEV: validate the existing asynchronous, restart-safe reassignment worker on new tag-only products. Fix remaining incompleteness in its own membership audit and secure the background job schema.
2. DEV: separately prove a controlled legacy-manual variant-selection removal and restoration, without touching production sources.
3. Production: migrate job schema, full status/attention-required UI, recovery playbook, rate and source checks, and approve separate feature flags. **Do not merge DEV-only job recovery or source-rehearsal controllers to master.**
4. Only after a controlled pilot, allow the client to change shipping policy on already-listed products. Publishing new parts needs none of these steps.
