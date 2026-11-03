/*
 PRODUCTION SHIPPING JOB PRE-FLIGHT — READ ONLY.
 Database: Site_2024. Execute manually in Azure SQL or SSMS before
 considering any Shopify shipping reassignment write flags.
 Does NOT create tables, change SQL values, or call Shopify.
 Save/export output with the release record.
*/
USE [Site_2024];
GO
SET NOCOUNT ON;
IF DB_NAME() <> N'Site_2024'
    THROW 51240, 'Expected production database Site_2024.', 1;
IF OBJECT_ID(N'dbo.Parts',N'U') IS NULL OR
   OBJECT_ID(N'dbo.ShippingPolicies',N'U') IS NULL OR
   OBJECT_ID(N'dbo.Available',N'U') IS NULL
    THROW 51241, 'Production inventory prerequisites missing.', 1;
IF COL_LENGTH(N'dbo.ShippingPolicies',N'ShopifyShippingCollectionGid') IS NULL
    THROW 51242, 'Markets shipping collection mapping is missing.', 1;

SELECT DB_NAME() AS VerifiedDatabase,
       SYSUTCDATETIME() AS VerifiedUtc,
       CASE WHEN OBJECT_ID(N'dbo.ShopifyShippingReassignmentJobs',N'U') IS NULL
            THEN N'MISSING - apply reviewed production queue migration'
            ELSE N'PRESENT' END AS ShippingQueueTable;

-- The queue's hardcoded AvailableId=1 and nonpurchasable Unavailable status
-- must agree with the production status table before any job can be queued.
SELECT Id, [Status], IsCustomerVisible, IsPurchasable
FROM dbo.Available ORDER BY Id;

-- Every ACTIVE checkout policy must have exactly one intended Shopify
-- collection mapping. Fix any NULL before enabling jobs.
SELECT Id, [Name], IsActive, AllowsOnlineCheckout,
       ShopifyShippingCollectionGid
FROM dbo.ShippingPolicies
WHERE IsActive=1 AND AllowsOnlineCheckout=1 ORDER BY Id;

SELECT Id AS UnmappedCheckoutPolicyId, [Name]
FROM dbo.ShippingPolicies
WHERE IsActive=1 AND AllowsOnlineCheckout=1 AND
      NULLIF(LTRIM(RTRIM(ShopifyShippingCollectionGid)), N'') IS NULL;

-- Identify unintended duplicate product mappings in production inventory.
SELECT ShopifyProductId, COUNT_BIG(*) AS LocalPartCount
FROM dbo.Parts
WHERE ShopifyProductId IS NOT NULL
GROUP BY ShopifyProductId
HAVING COUNT_BIG(*)>1;

-- Existing jobs are examined; never deleted/reinitialized.
IF OBJECT_ID(N'dbo.ShopifyShippingReassignmentJobs',N'U') IS NOT NULL
BEGIN
    EXEC(N'SELECT COUNT_BIG(*) AS ExistingJobs,
                  SUM(CASE WHEN IsOpen=1 THEN 1 ELSE 0 END) AS OpenJobs
           FROM dbo.ShopifyShippingReassignmentJobs;');
    EXEC(N'SELECT TOP (20) JobId, PartId, FromPolicyId, ToPolicyId,
                  Status, IsOpen, CreatedUtc, UpdatedUtc
           FROM dbo.ShopifyShippingReassignmentJobs
           ORDER BY CreatedUtc DESC;');
END;
GO
