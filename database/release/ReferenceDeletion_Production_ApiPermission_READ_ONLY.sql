-- Read-only. Operator replaces this placeholder in a reviewed local copy.
-- Authenticate directly as the actual production API identity; do not impersonate it.
IF DB_NAME() <> N'Site_2024'
    THROW 50530, 'Expected production database Site_2024. Stop.', 1;
DECLARE @ExpectedApiDatabaseUser sysname=N'REPLACE_WITH_ACTUAL_API_DATABASE_USER';
IF @ExpectedApiDatabaseUser=N'REPLACE_WITH_ACTUAL_API_DATABASE_USER'
    OR USER_NAME()<>@ExpectedApiDatabaseUser
    THROW 50531, 'Confirm the actual production API database principal before checking permissions.', 1;
SELECT DB_NAME() AS DatabaseName, USER_NAME() AS DatabaseUser,
    HAS_PERMS_BY_NAME(N'dbo.ReferenceData_DeleteSafe',N'OBJECT',N'EXECUTE') AS CanExecute;
IF ISNULL(HAS_PERMS_BY_NAME(N'dbo.ReferenceData_DeleteSafe',N'OBJECT',N'EXECUTE'),0)<>1
    THROW 50532, 'API identity lacks effective EXECUTE; DBA review/grant is required.', 1;
-- Common-owner dependencies need no new direct table/function grants. A DBA must
-- verify the ownership chain, triggers, explicit DENY and existing caller contracts.
