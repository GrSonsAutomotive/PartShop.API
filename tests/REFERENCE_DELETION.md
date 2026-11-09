# Reference deletion review and validation

The migration was tested in newly created disposable databases on local SQL
Server. On November 8, 2026, the operator confirmed that the revised migration
was applied successfully to Site_2024_Dev, all six SQL objects exist, and the
read-only dependency preview returned the expected results. Production has not
been changed by this feature preparation.

## DEV deployment readiness

Both feature branches target `dev`. Deploy the API before using the React deletion
controls. Production settings and workflows are unchanged. The existing React
DEV workflow runs on pull requests and may publish a DEV preview; the API DEV
workflow deploys on a push to `dev`, not on draft PR creation.

Current local validation: Release API solution and test harness builds passed,
22 API authorization/contract checks passed, and 7 React tests across 2 suites
passed. The optimized React build passed with existing lint/CSS/tooling warnings;
the API build reports existing nullable/package warnings. The recorded 154 SQL
integration results below are from the earlier disposable-database run. They were
not rerun during deployment preparation because no database changes were authorized.

### SQL access prerequisite (apply separately; not executed here)

The migration does not grant permissions. The deployed DEV API database principal
and its effective permissions are not available in local configuration, so access
is **not yet independently confirmed**. The successful operator preview confirms
API access only if it used the deployed API's actual database identity.

Run `tests/reference-deletion-permissions.sql` through a connection authenticated
as the DEV API database account. It only reads permission and ownership metadata.
Require `CanExecute = 1` and the same effective owner for the procedure, both
functions, and all seven referenced tables. A DBA should inspect any missing
metadata or ownership differences before deployment.

If effective EXECUTE is missing, a DBA should apply this grant in
`Site_2024_Dev`, replacing the placeholder with the actual database user or existing
API execution role (do not grant to `public`):

```sql
GRANT EXECUTE ON OBJECT::dbo.ReferenceData_DeleteSafe TO [DEV_API_DATABASE_USER_OR_ROLE];
```

The procedure uses static SQL against objects in the same database. With a common
owner, ownership chaining covers the two helper functions and underlying tables;
no additional direct function/table grants are required. The three legacy wrapper
procedures need no new grants for this API, which calls DeleteSafe directly.
Existing callers of wrappers retain their existing entry-point permissions.
Do not add broad table DELETE, schema EXECUTE, or db_owner permissions as a shortcut.
Reference: [Microsoft ownership chaining documentation](https://learn.microsoft.com/en-us/sql/relational-databases/tutorial-ownership-chains-and-context-switching).

## Locking decision

Seven forced `TABLOCKX` scans are unnecessary for atomic deletion. Final deletion
now sets procedure-scoped `SERIALIZABLE` isolation, acquires `UPDLOCK` on the target
category/model/Make rows, and holds all dependency read locks until commit. This
protects both existing references and insertion ranges, including when
`READ_COMMITTED_SNAPSHOT` is ON. It works against ordinary SQL writers, without
requiring them to participate in application locks.

| Read | Protection and remaining scope |
| --- | --- |
| Target category or global model | Update lock on its key; no need to lock every lookup table exclusively. |
| Make associations / manufacturer | Target update locks and serializable predicate protection; includes concurrent company associations before target materialization. |
| Parts / additional categories / fitments | Serializable dependency reads prevent referenced inserts or retargeting updates from slipping between check and delete. Existing indexes help determine lock scope. |
| Discount rules | Serializable read includes all historical rules. Normalization and tag predicates are non-sargable and may scan/lock the whole rules index. |

This is narrower locking where the available access paths permit it, not a promise
of exclusively row locks. The snapshot lacks useful indexes for some primary
Parts references and normalized manufacturer/rule predicates. SQL Server can take
broad range/shared locks or escalate. A held category deletion was tested to allow
an unrelated category update, unlike the previous forced table-exclusive lock.
Further predictable performance improvements would require indexed, sargable
company and normalized-rule keys plus writer-path validation; those schema changes
are deliberately outside this migration. No `ROWLOCK`, `READPAST`, `NOLOCK`, or
cooperative application-lock shortcut is used.

Deletion uses LOW deadlock priority so business writers can win. SQL error 1205
rolls back the victim and maps to HTTP 503, `Retry-After: 1`, and
`code: ReferenceDataBusy`. There is no automatic DELETE retry and no stale
dependency-count payload. Refresh the preview before explicitly retrying. Missing
targets return 404; existing referenced targets return 409.

Guarantees have a serialization point: an ordinary discount writer that starts
after deletion commits can still create a stale SourceId because discount sources
have no lookup FK. Neither the previous seven locks nor the new locks prevent
future invalid writes. Preventing those requires validation in every discount
writer (or a separate schema constraint design). Concurrent writers that commit
before the final check are included; writers trying to change a protected range
wait for transaction completion.

## Matching and manufacturer behavior

Names and tags are conservatively normalized by removing punctuation and
non-ASCII characters on both sides. Collisions can block deletion and require
manual review. They never cause another category or global model to be removed.
RuleType/SourceId checks, company siblings, shared model names, and historical
rules remain protected. CustomTag rules using the full managed tag also count.

The original tag comparison incorrectly treated arbitrary short prefixes such as
`Cat`, `Category_Air`, or `Model_Un` as full-name dependencies. It now compares
the entire normalized tag. Source names are limited to 128 characters, so their
managed tags are below Shopify's 255-character limit and need no truncation rule.

Manufacturer deletion is one transactional DELETE of the materialized target
set. Any dependency blocks the entire set. An AFTER DELETE trigger that deliberately
throws was tested to restore every target row; successful manufacturer deletion
preserves global models, including shared ones. A new unused association that
commits before target selection is correctly included in that deletion set.

## Run the real SQL suite

Requirements: Windows, .NET 8 SDK/runtime, restored project dependencies, and a
local default SQL Server instance reachable through shared memory `lpc:localhost`.
Use Windows integrated authentication with permission to create/drop a test
database and inspect test-session blocking through `sys.dm_exec_requests`
(`VIEW SERVER STATE`, or `VIEW SERVER PERFORMANCE STATE` on SQL Server 2022).
A local sysadmin test account meets these requirements. No Azure credentials,
application connection strings, or production settings are read.

Run from `C:\Projects\GR&Sons\New-Site-2024\Site_2024`:

```powershell
dotnet restore tests/ReferenceDeletion.Tests/ReferenceDeletion.Tests.csproj --ignore-failed-sources -p:NuGetAudit=false
dotnet build tests/ReferenceDeletion.Tests/ReferenceDeletion.Tests.csproj --no-restore
dotnet tests/ReferenceDeletion.Tests/bin/Debug/net8.0/ReferenceDeletion.Tests.dll --sql
```

The runner checks the server's machine name, generates a new
`ReferenceDeletionTests_<GUID>` database, refuses to reuse an existing database,
and marks ownership with an extended property. It applies only the focused
migration and minimal fixture schema. It never accepts a database/server argument.
It tests READ_COMMITTED_SNAPSHOT OFF and ON only in that database. Cleanup checks
both the exact generated name and ownership marker before dropping it, including
on test failure. If the ownership marker cannot be verified, cleanup refuses to
drop anything and reports the database name for manual inspection.

The runner needs a usable local instance; SQLCMD/LocalDB executables alone are
insufficient. If unavailable, install SQL Server Developer/Express locally (or use
an isolated Windows VM with the local default instance), then run the commands
there. Do not retarget the runner at an application database. A container or
non-default instance requires a separately reviewed local-only connection change.
This suite must not be replaced by the mocked API checks below.

## Actual results

Final run: **154/154 real SQL/API integration checks passed**, with each case run
under READ_COMMITTED_SNAPSHOT OFF and ON. Every result, the generated database
name, and successful cleanup are in `reference-deletion-results.txt`.

| Cases across both isolation modes | Passed |
| --- | ---: |
| SQL deletion/dependency/collision/tag/history/shared-model/inventory/sales/ordering assertions | 64 |
| Repeated migration: seeded data/schema identities and module definitions unchanged | 4 |
| Concurrent inserts, retargeting updates, manufacturer phantoms, FK enforcement and unrelated writes | 36 |
| Manufacturer failure rollback, complete success, preserved models and inventory | 8 |
| Actual HTTP requests backed by SQL: 200/404/409, previews, real deadlock 503 and recovery | 42 |
| Total | 154 |

The first SQL run stopped at an incorrect test expectation: a newly committed,
unused manufacturer association was expected to block deletion. Correct behavior
includes it in the target set and deletes both unused associations. The test was
corrected; the subsequent complete runs passed. The first harness build also
required replacing an unavailable SqlConnection property with `SELECT @@SPID`.

The SQL schema fixture reproduces relevant FK/index shapes; it is not a copy of
the complete Azure schema or data. Production-size lock escalation, deployment
permissions/schema drift, and load behavior remain outside these local tests.
Repeated CREATE OR ALTER can update SQL module modify timestamps; data, object
identities, index identities and definition text are checked for unexpected changes.

Additional validation: API/test-project build passed (existing NU1701 package
compatibility warning); all 22 isolated mocked API checks passed; all 7 React tests
across 2 suites passed. The mocked checks verify authorization without SQL and are
reported separately from the 154 real integration checks.

```powershell
# Optional API-only authorization/contract suite; no SQL connections:
dotnet tests/ReferenceDeletion.Tests/bin/Debug/net8.0/ReferenceDeletion.Tests.dll
```

No deployment or application-database migration is authorized by these test runs.
