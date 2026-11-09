-- Read-only structural verification. This does not invoke the deletion procedure.
SET NOCOUNT ON;
IF DB_NAME() <> N'Site_2024'
    THROW 50520, 'Expected production database Site_2024. Stop.', 1;

DECLARE @required TABLE (ObjectName sysname, ExpectedType char(2));
INSERT @required VALUES
    (N'ReferenceData_Normalize','FN'), (N'ReferenceData_TagMatches','FN'),
    (N'ReferenceData_DeleteSafe','P'), (N'Catagory_Delete','P'),
    (N'Make_Delete','P'), (N'Model_Delete','P');
IF EXISTS (SELECT 1 FROM @required r LEFT JOIN sys.objects o
    ON o.schema_id=SCHEMA_ID(N'dbo') AND o.name COLLATE DATABASE_DEFAULT=r.ObjectName
    LEFT JOIN sys.sql_modules m ON m.object_id=o.object_id
    WHERE o.object_id IS NULL OR o.type COLLATE DATABASE_DEFAULT<>r.ExpectedType OR m.definition IS NULL)
    THROW 50521, 'Expected all six unencrypted reference deletion SQL modules.', 1;

DECLARE @owner int = (SELECT COALESCE(principal_id,SCHEMA_OWNER)
    FROM (SELECT o.principal_id,s.principal_id AS SCHEMA_OWNER FROM sys.objects o
        JOIN sys.schemas s ON s.schema_id=o.schema_id
        WHERE o.object_id=OBJECT_ID(N'dbo.ReferenceData_DeleteSafe')) owners);
IF EXISTS (SELECT 1 FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
    WHERE s.name=N'dbo' AND o.name IN
        (N'ReferenceData_Normalize',N'ReferenceData_TagMatches',N'ReferenceData_DeleteSafe',
         N'Catagory_Delete',N'Make_Delete',N'Model_Delete',N'Catagory',N'Model',N'Make',
         N'Parts',N'PartCategories',N'PartFitments',N'AdminDiscountCodeRules')
    AND COALESCE(o.principal_id,s.principal_id)<>@owner)
    THROW 50522, 'Ownership chain differs: DBA must review before API activation.', 1;

SELECT r.ObjectName, o.type_desc, USER_NAME(COALESCE(o.principal_id,s.principal_id)) AS EffectiveOwner,
    m.definition
FROM @required r JOIN sys.objects o ON o.object_id=OBJECT_ID(N'dbo.'+r.ObjectName)
JOIN sys.schemas s ON s.schema_id=o.schema_id
JOIN sys.sql_modules m ON m.object_id=o.object_id ORDER BY r.ObjectName;
SELECT USER_NAME() AS CurrentDatabaseUser,
    HAS_PERMS_BY_NAME(N'dbo.ReferenceData_DeleteSafe',N'OBJECT',N'EXECUTE') AS CurrentIdentityCanExecute;
-- Compare definitions with the approved migration and preserve the results privately.
-- Run the separate API-identity permission check; the current DBA's result is insufficient.
