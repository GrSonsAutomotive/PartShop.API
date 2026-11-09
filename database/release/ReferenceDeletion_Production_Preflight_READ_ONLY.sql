-- Read-only DBA preflight. No application procedures or Shopify operations run.
-- Connect explicitly to Site_2024. Export results privately; do not publish them.
SET NOCOUNT ON;
IF DB_NAME() <> N'Site_2024'
    THROW 50510, 'Expected production database Site_2024. Stop.', 1;
IF HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION') <> 1
    THROW 50511, 'DBA metadata visibility is required for this preflight.', 1;

SELECT DB_NAME() AS DatabaseName, USER_NAME() AS ReviewingDatabaseUser,
    DATABASEPROPERTYEX(DB_NAME(), 'Updateability') AS Updateability,
    is_read_committed_snapshot_on
FROM sys.databases WHERE database_id = DB_ID();

DECLARE @columns TABLE (TableName sysname, ColumnName sysname, IsText bit, MaxCharacters int);
INSERT @columns VALUES
    (N'Catagory', N'id', 0, NULL), (N'Catagory', N'name', 1, 128),
    (N'Model', N'id', 0, NULL), (N'Model', N'name', 1, 128),
    (N'Make', N'id', 0, NULL), (N'Make', N'company', 1, 128), (N'Make', N'modelId', 0, NULL),
    (N'Parts', N'catagoryId', 0, NULL), (N'Parts', N'makeId', 0, NULL),
    (N'PartCategories', N'CatagoryId', 0, NULL), (N'PartFitments', N'MakeId', 0, NULL),
    (N'AdminDiscountCodeRules', N'RuleType', 1, 4000),
    (N'AdminDiscountCodeRules', N'SourceId', 0, NULL),
    (N'AdminDiscountCodeRules', N'RuleValue', 1, 4000),
    (N'AdminDiscountCodeRules', N'ShopifyTag', 1, 4000);

DECLARE @schema TABLE (TableName sysname, ColumnName sysname, TypeName sysname,
    MaxLength smallint, IsNullable bit, IsCompatible bit);
INSERT @schema
SELECT r.TableName, r.ColumnName, TYPE_NAME(c.system_type_id), c.max_length, c.is_nullable,
    CASE WHEN t.object_id IS NULL OR c.column_id IS NULL THEN 0
         WHEN r.IsText = 0 AND TYPE_NAME(c.system_type_id) = N'int' THEN 1
         WHEN r.IsText = 1 AND TYPE_NAME(c.system_type_id) IN (N'nvarchar',N'nchar',N'varchar',N'char')
              AND c.max_length > 0 AND
              c.max_length / CASE WHEN TYPE_NAME(c.system_type_id) IN (N'nvarchar',N'nchar') THEN 2 ELSE 1 END
                  <= r.MaxCharacters THEN 1 ELSE 0 END
FROM @columns r
LEFT JOIN sys.tables t ON t.schema_id = SCHEMA_ID(N'dbo') AND t.name COLLATE DATABASE_DEFAULT = r.TableName
LEFT JOIN sys.columns c ON c.object_id = t.object_id AND c.name COLLATE DATABASE_DEFAULT = r.ColumnName;
SELECT * FROM @schema ORDER BY TableName, ColumnName;
IF EXISTS (SELECT 1 FROM @schema WHERE IsCompatible = 0)
    THROW 50512, 'Missing or incompatible reference-deletion table/column. Review schema before installation.', 1;

DECLARE @modules TABLE (ObjectName sysname, ExpectedType char(2));
INSERT @modules VALUES
    (N'ReferenceData_Normalize', 'FN'), (N'ReferenceData_TagMatches', 'FN'),
    (N'ReferenceData_DeleteSafe', 'P'), (N'Catagory_Delete', 'P'),
    (N'Make_Delete', 'P'), (N'Model_Delete', 'P');
SELECT r.ObjectName, o.type_desc,
    CASE WHEN o.object_id IS NOT NULL THEN USER_NAME(COALESCE(o.principal_id, s.principal_id)) END AS EffectiveOwner,
    m.is_schema_bound, m.execute_as_principal_id, m.definition
FROM @modules r
LEFT JOIN sys.schemas s ON s.name = N'dbo'
LEFT JOIN sys.objects o ON o.schema_id = s.schema_id AND o.name COLLATE DATABASE_DEFAULT = r.ObjectName
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
ORDER BY r.ObjectName;
IF EXISTS (SELECT 1 FROM @modules r JOIN sys.objects o
    ON o.schema_id = SCHEMA_ID(N'dbo') AND o.name COLLATE DATABASE_DEFAULT = r.ObjectName
    LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
    WHERE o.type COLLATE DATABASE_DEFAULT <> r.ExpectedType OR m.definition IS NULL)
    THROW 50513, 'An existing module has an incompatible type or hidden/encrypted definition.', 1;

-- Preserve these definitions, signatures and grants in the DBA rollback record.
SELECT OBJECT_NAME(p.object_id) AS ModuleName, p.name AS ParameterName,
    TYPE_NAME(p.system_type_id) AS TypeName, p.max_length, p.is_output
FROM sys.parameters p JOIN @modules r ON OBJECT_ID(N'dbo.' + r.ObjectName) = p.object_id
ORDER BY ModuleName, p.parameter_id;
IF EXISTS (SELECT 1 FROM @modules r JOIN sys.procedures o
    ON o.schema_id = SCHEMA_ID(N'dbo') AND o.name COLLATE DATABASE_DEFAULT = r.ObjectName
    WHERE r.ObjectName IN (N'Catagory_Delete',N'Make_Delete',N'Model_Delete') AND
      ((SELECT COUNT(*) FROM sys.parameters p WHERE p.object_id=o.object_id) <> 1
       OR NOT EXISTS (SELECT 1 FROM sys.parameters p WHERE p.object_id=o.object_id
           AND LOWER(p.name)=N'@id' AND TYPE_NAME(p.system_type_id)=N'int' AND p.is_output=0)))
    THROW 50514, 'A legacy delete procedure has an incompatible parameter contract.', 1;

SELECT OBJECT_NAME(dp.major_id) AS ObjectName, USER_NAME(dp.grantee_principal_id) AS Grantee,
    dp.state_desc, dp.permission_name
FROM sys.database_permissions dp
WHERE dp.class = 1 AND dp.major_id IN (SELECT OBJECT_ID(N'dbo.' + ObjectName) FROM @modules);

-- Metadata counts are estimates, not a full inventory scan. They do not inspect row data.
SELECT t.name AS TableName, USER_NAME(COALESCE(t.principal_id,s.principal_id)) AS EffectiveOwner,
    SUM(p.rows) AS ApproximateRows
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
LEFT JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1)
WHERE s.name=N'dbo' AND t.name COLLATE DATABASE_DEFAULT IN (SELECT TableName FROM @columns)
GROUP BY t.name, t.principal_id, s.principal_id ORDER BY t.name;

SELECT t.name AS TableName, i.name AS IndexName, i.is_unique, i.is_disabled, i.filter_definition,
    c.name AS ColumnName, ic.key_ordinal, ic.is_included_column
FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE t.schema_id=SCHEMA_ID(N'dbo') AND t.name COLLATE DATABASE_DEFAULT IN (SELECT TableName FROM @columns)
ORDER BY t.name, i.index_id, ic.key_ordinal, ic.index_column_id;

SELECT OBJECT_NAME(tr.parent_id) AS TableName, tr.name AS TriggerName, tr.is_disabled,
    sm.definition
FROM sys.triggers tr LEFT JOIN sys.sql_modules sm ON sm.object_id=tr.object_id
WHERE tr.parent_id IN (SELECT OBJECT_ID(N'dbo.' + TableName) FROM @columns);

-- DBA review: all static dependency/module owners must match; assess any triggers,
-- serializable dependency scans, normalized-rule scans and production-size locking.
-- Schema-level/database-level/role permissions and explicit DENY also affect access.
-- A DBA's successful preflight is NOT proof of the deployed API identity's permission.
