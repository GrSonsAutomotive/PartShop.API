/*
DEV parity preflight -- READ ONLY.
DO NOT RUN against production. No Shopify calls, schema changes, or data updates.
Review the server/database values BEFORE any subsequent DEV migration.
*/
SET NOCOUNT ON;
IF DB_NAME() <> N'Site_2024_Dev'
    THROW 51410, 'Blocked: expected exactly Site_2024_Dev. No changes made.', 1;

SELECT
    DB_NAME() AS CurrentDatabase,
    CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)) AS SqlServerName,
    SYSUTCDATETIME() AS CheckedAtUtc;

SELECT 'dbo.Parts' AS CheckName,
    CASE WHEN OBJECT_ID(N'dbo.Parts', N'U') IS NOT NULL THEN 1 ELSE 0 END AS Ready
UNION ALL SELECT 'dbo.ShippingPolicies',
    CASE WHEN OBJECT_ID(N'dbo.ShippingPolicies', N'U') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL SELECT 'Shipping collection mapping column',
    CASE WHEN COL_LENGTH(N'dbo.ShippingPolicies', N'ShopifyShippingCollectionGid') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL SELECT 'Durable shipping reassignment queue',
    CASE WHEN OBJECT_ID(N'dbo.ShopifyShippingReassignmentJobs', N'U') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL SELECT 'Unique open shipping job per part',
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.ShopifyShippingReassignmentJobs')
          AND name = N'UX_ShippingJobs_OneOpenPerPart'
    ) THEN 1 ELSE 0 END
UNION ALL SELECT 'Shipping job claim index',
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.ShopifyShippingReassignmentJobs')
          AND name = N'IX_ShippingJobs_Claim'
    ) THEN 1 ELSE 0 END
UNION ALL SELECT 'Pending/Available status table',
    CASE WHEN OBJECT_ID(N'dbo.Available',N'U') IS NOT NULL THEN 1 ELSE 0 END;

IF OBJECT_ID(N'dbo.Available',N'U') IS NOT NULL
    SELECT Id, [Status] FROM dbo.Available
    WHERE [Status] IN (N'Available', N'Pending')
    ORDER BY [Status];

IF OBJECT_ID(N'dbo.ShopifyShippingReassignmentJobs',N'U') IS NOT NULL
    EXEC sp_executesql N'SELECT Status, COUNT(*) AS JobCount
        FROM dbo.ShopifyShippingReassignmentJobs
        GROUP BY Status ORDER BY Status';
