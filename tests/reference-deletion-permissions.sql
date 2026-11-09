-- Read-only verification. Connect as the deployed DEV API database account.
-- No EXECUTE AS, procedure invocation, grants, or application data writes.
IF DB_NAME() <> N'Site_2024_Dev'
    THROW 50500, 'Connect to Site_2024_Dev with the DEV API identity.', 1;

SELECT DB_NAME() AS DatabaseName, USER_NAME() AS DatabaseUser,
    HAS_PERMS_BY_NAME(N'dbo.ReferenceData_DeleteSafe', N'OBJECT', N'EXECUTE') AS CanExecute;

SELECT required.ObjectName, o.type_desc,
    CASE WHEN o.object_id IS NOT NULL
        THEN USER_NAME(COALESCE(o.principal_id, s.principal_id)) END AS EffectiveOwner
FROM (VALUES
    (N'ReferenceData_DeleteSafe'), (N'ReferenceData_Normalize'),
    (N'ReferenceData_TagMatches'), (N'Catagory'), (N'Model'), (N'Make'),
    (N'Parts'), (N'PartCategories'), (N'PartFitments'),
    (N'AdminDiscountCodeRules')
) required(ObjectName)
LEFT JOIN sys.schemas s ON s.name = N'dbo'
LEFT JOIN sys.objects o ON o.schema_id = s.schema_id AND o.name = required.ObjectName
ORDER BY required.ObjectName;
