# Reference deletion production release — HOLD

This is an operator-reviewed plan, not authorization to run SQL or deploy.
No production configuration, shipping operations, or existing shipping migration
is changed by this release. Keep SQL results and environment details private.

## Prerequisites and review

1. Confirm the final API/frontend release revisions, approval owner and maintenance
   window. Retain current verified production artifacts and a recoverable database
   backup/restore point. Export the six affected SQL module definitions and existing
   object/role grants securely before installation; record reference IDs/data needed
   for selective recovery. Do not assume a branch head equals the live artifact.
2. Confirm the connection target is production `Site_2024` and identify the deployed
   API database principal from the actual running API configuration. A personal DBA
   login and an application role such as AdminHigh do not establish that identity.
   No Azure configuration or feature-flag change is required or authorized here.
3. With DBA metadata visibility, review/run only
   `ReferenceDeletion_Production_Preflight_READ_ONLY.sql`. It checks identity,
   required column/type/length compatibility, existing module types and legacy
   parameter contracts, ownership, grants, estimated record counts, indexes and
   triggers. It does not invoke application procedures or scan application row data.
4. Verify common effective owners for static dependencies and modules, and review
   triggers. Assess indexes on primary category/make references, additional category
   and fitment references, and discount rules. Normalized rule/company matching is
   non-sargable and can scan broadly; serializable deletion may block or escalate.
   Metadata row counts are estimates, not load testing. Validate realistic scale
   and execution plans in an isolated copy before approving installation.
5. Stop for missing/hidden metadata, incompatible types/contracts, ownership drift,
   unknown current grants, or unresolved locking/recovery concerns. Production
   verification has not been performed by preparing these files.

## Guarded installation — separate explicit approval required

The required module migration is
`database/migrations/20261107_01_ReferenceDataSafeDeletion.sql`. Its contents are
unchanged from the approved feature migration; do not run it bare against production.

After explicit approval, create a reviewed operator copy of
`ReferenceDeletion_Production_Install.sql` and change only its
`ReferenceDeletionApproved` variable from `NO` to `YES`. Record the approval and
script hashes. Run that copy in a **dedicated SQLCMD-mode connection**, from the API
repository root so the relative include paths resolve, explicitly targeting
`Site_2024`. With the CLI, use error-stop handling (`-b`) as well as the script's
`:on error exit`. Use the approved authentication method; never put passwords in
committed scripts or command history.

The wrapper validates the database and approval before opening a transaction,
includes the read-only schema preflight, installs all six modules in one transaction,
and includes read-only post-install structural/ownership checks before committing.
It creates/alters only Normalize, TagMatches, DeleteSafe, Catagory_Delete,
Make_Delete and Model_Delete. No seed data, permission grant, shipping migration,
inventory update or Shopify operation is included.

Stop on the first error. A CLI error exit closes the dedicated connection and any
uncommitted transaction rolls back. In SSMS SQLCMD mode, explicitly ROLLBACK or
disconnect if an aborted batch leaves the connection open. Never continue or COMMIT
manually after a failed installation. Compare resulting definitions to the approved
migration (outer include-boundary blank lines may differ).

## Permission and post-install checks

Run `ReferenceDeletion_Production_PostInstall_READ_ONLY.sql` again as the DBA if
needed. It checks all six module types/definitions and effective owners and performs
no application procedure call. Its current-identity EXECUTE result belongs to the
connected DBA and is not proof of API access.

The API identity requires effective EXECUTE on `dbo.ReferenceData_DeleteSafe`.
If missing, a DBA may apply this narrow grant under separate approval:

```sql
-- Proposal only. Verify Site_2024 and substitute the actual API user/existing role.
GRANT EXECUTE ON OBJECT::dbo.ReferenceData_DeleteSafe
TO [ACTUAL_PRODUCTION_API_DATABASE_USER_OR_ROLE];
```

Authenticate directly with the deployed API database identity. In a reviewed local
copy of `ReferenceDeletion_Production_ApiPermission_READ_ONLY.sql`, replace the
expected database-user placeholder and run the check. It rejects a different user
or missing effective EXECUTE. Review role inheritance/explicit DENY with the DBA.
With common owners and static SQL, no direct helper-function/table grants are
needed; existing legacy-wrapper callers retain their prior entry-point permissions.
Do not grant db_owner, public EXECUTE, broad schema EXECUTE or table DELETE.

Existing production shipping migrations are baseline prerequisites, not part of
this new installation. Leave them unchanged; independently verify prior readiness
when needed. Never run DEV shipping scripts, fixture SQL or the SQL test harness on
production. This release does not authorize a Shopify cutover or worker activation.

## Deployment after approval

1. Confirm production app settings, database identity, frontend API host and current
   Shopify/shipping flags remain as approved; do not copy DEV configuration.
2. Complete isolated candidate builds/tests, route checks, clean frontend install,
   and existing production publishing/inventory/checkout/queue interface review.
3. Resolve or explicitly accept separately reported security findings. Current
   repository dependency versions are preserved apart from the missing lock entry.
4. Coordinate the unchanged production workflow triggers. Draft status alone does
   not prevent a frontend PR preview deployment. Review-only heads use `[skip ci]`;
   these skipped checks are not successful CI. Do not remove suppression or merge
   until the exact validation/deployment sequence is explicitly approved.
5. Install and verify approved SQL and permissions before deploying the new API.
   Deploy the API before the frontend. Check startup, canonical identity routing,
   AdminHigh/low authorization and read-only dependency previews. Do not delete live
   reference records as a smoke test.
6. Deploy the frontend; check production API host, Catalog Setup, button alignment,
   ordering, Make/Model selection, Pending publishing, inventory browsing and checkout.
   Monitor errors, 503/deadlocks, blocking, existing jobs and listing availability.

## Rollback

1. Stop new releases/reference edits and record current artifact/SQL/job state.
   Do not blindly retry uncertain Shopify work or interrupt active jobs.
2. Restore the verified previous frontend artifact first, then the prior API artifact
   if necessary. Use an established approved restore/redeploy process; no staging
   slot or artifact-retention guarantee is assumed. Do not reset shared branch
   history. A revert PR requires review because a production push triggers deployment.
3. Prefer retaining the additive helpers and safe wrapper signatures while rolling
   application code back. Review old callers' handling of blocked deletions. Avoid
   restoring unsafe delete behavior or dropping dependencies as a routine rollback.
4. If module/grant rollback is necessary, a DBA restores the captured definitions
   and release-specific grants under separate approval and checks dependencies.
   No destructive down migration is provided or authorized.
5. Code rollback does not restore deleted reference data. Restore a backup to a
   separate database, compare exact IDs/relationships, and selectively recover under
   DBA approval. Do not discard newer inventory, orders or valid reference writes
   with an unreviewed in-place full restore.
6. Verify inventory, checkout, publishing, queue state, auth and logs. Reconcile
   external outcomes before resuming any interrupted operation. Record recovery
   evidence and obtain approval before another release attempt.
