/*
Site_2024 / PRODUCTION — READ-ONLY SHIPPING BASELINE.
Safe to run before any migrations or Shopify upgrade.
Save/export the result sets as a before-snapshot.

This script performs ZERO updates.
*/
USE [Site_2024];
GO
SET NOCOUNT ON;
IF DB_NAME() <> N'Site_2024'
    THROW 51220, 'Expected production database Site_2024.', 1;

SELECT DB_NAME() AS DatabaseName, SYSUTCDATETIME() AS SnapshotUtc;

SELECT COUNT_BIG(*) AS TotalParts,
       SUM(CASE WHEN ShopifyProductId IS NOT NULL THEN 1 ELSE 0 END) AS ShopifyLinked,
       SUM(CASE WHEN ShopifyProductId IS NULL THEN 1 ELSE 0 END) AS LocalOnly,
       SUM(CASE WHEN Quantity > 0 THEN 1 ELSE 0 END) AS InStock,
       SUM(CASE WHEN AvailableId = 1 THEN 1 ELSE 0 END) AS Available
FROM dbo.Parts;

SELECT p.ShippingPolicyId, sp.Name AS ShippingPolicy,
       sp.ShopifyProfileId, sp.IsActive, sp.AllowsOnlineCheckout,
       COUNT(*) AS Parts,
       SUM(CASE WHEN p.ShopifyProductId IS NOT NULL THEN 1 ELSE 0 END) AS LinkedShopifyProducts,
       SUM(CASE WHEN p.ShopifyProductId IS NULL THEN 1 ELSE 0 END) AS LocalOnly,
       SUM(CASE WHEN p.AvailableId = 1 THEN 1 ELSE 0 END) AS Available,
       SUM(CASE WHEN p.Quantity > 0 THEN 1 ELSE 0 END) AS InStock
FROM dbo.Parts p
LEFT JOIN dbo.ShippingPolicies sp ON sp.Id=p.ShippingPolicyId
GROUP BY p.ShippingPolicyId, sp.Name, sp.ShopifyProfileId,
         sp.IsActive, sp.AllowsOnlineCheckout
ORDER BY p.ShippingPolicyId;

-- Duplicate Shopify mappings must be resolved before any tag backfill.
SELECT ShopifyProductId, COUNT(*) AS DuplicatePartCount
FROM dbo.Parts WHERE ShopifyProductId IS NOT NULL
GROUP BY ShopifyProductId HAVING COUNT(*) > 1;

-- Missing Shopify products (e.g. Policy 14's local-only part).
SELECT p.Id, p.ShippingPolicyId, p.Quantity, p.AvailableId
FROM dbo.Parts p
WHERE p.ShopifyProductId IS NULL
ORDER BY p.Id;

-- Verify all statuses including the service's hardcoded AvailableId=1 contract.
SELECT Id, [Status], IsCustomerVisible, IsPurchasable
FROM dbo.Available ORDER BY Id;

-- Migration-safe object checks; no assumption that mapping/jobs are installed.
SELECT
    CASE WHEN COL_LENGTH(N'dbo.ShippingPolicies',
        N'ShopifyShippingCollectionGid') IS NULL THEN N'MISSING' ELSE N'PRESENT' END
        AS MappingColumn,
    CASE WHEN OBJECT_ID(N'dbo.ShopifyShippingReassignmentJobs', N'U') IS NULL
        THEN N'MISSING' ELSE N'PRESENT' END AS AsyncQueueTable;

-- Capture current stored procedure definition before changing it.
SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.ShippingPolicies_SelectAll'))
    AS CurrentShippingPoliciesSelectAllDefinition;
GO
