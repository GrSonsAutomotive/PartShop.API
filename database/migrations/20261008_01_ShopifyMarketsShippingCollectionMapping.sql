/*
Site_2024 — Shopify Market-driven Shipping migration, phase 2A
Run against Site_2024_Dev first in SSMS. DO NOT run against production until
the API migration is validated and the production release is approved.

Purpose:
- Preserve existing ShippingPolicies.ShopifyProfileId and all existing rows.
- Add a nullable Shopify collection GID for shipping-class collection mapping.
- Append that column to ShippingPolicies_SelectAll without changing the
  order of the original seven columns (backward-compatible with existing API).
- Provide an explicit update procedure for future mapping workflows.
- Do NOT switch shipping behavior or modify any Shopify store settings.

This script is safe to re-run. Requires Azure SQL / SQL Server support for
CREATE OR ALTER PROCEDURE and T-SQL GO batches (e.g. SSMS).
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ShippingPolicies', N'U') IS NULL
BEGIN
    THROW 51020, 'dbo.ShippingPolicies was not found. Stop and verify the database.', 1;
END;

IF COL_LENGTH(N'dbo.ShippingPolicies', N'ShopifyShippingCollectionGid') IS NULL
BEGIN
    ALTER TABLE dbo.ShippingPolicies
        ADD ShopifyShippingCollectionGid NVARCHAR(255) NULL;
END;
GO

-- The first seven selected columns must retain their current ordering.
-- ShippingPoliciesService.GetAll currently reads these seven by ordinal.
CREATE OR ALTER PROCEDURE dbo.ShippingPolicies_SelectAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
          Id
        , [Name]
        , ShopifyProfileId
        , IsActive
        , AllowsOnlineCheckout
        , DateCreated
        , DateModified
        , ShopifyShippingCollectionGid
    FROM dbo.ShippingPolicies
    WHERE IsActive = 1
    ORDER BY [Name];
END;
GO

-- This procedure is not called by the legacy API yet.
-- It will support mapping a local shipping policy to an existing or new
-- Shopify collection once the Markets integration has been tested.
CREATE OR ALTER PROCEDURE dbo.ShippingPolicies_UpdateShopifyShippingCollectionGid
    @Id INT,
    @ShopifyShippingCollectionGid NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.ShippingPolicies WHERE Id = @Id)
    BEGIN
        THROW 51021, 'Shipping policy not found.', 1;
    END;

    DECLARE @NormalizedGid NVARCHAR(255) =
        NULLIF(LTRIM(RTRIM(@ShopifyShippingCollectionGid)), N'');

    IF @NormalizedGid IS NOT NULL
       AND @NormalizedGid NOT LIKE N'gid://shopify/Collection/[0-9]%'
    BEGIN
        THROW 51022, 'Expected a Shopify Collection GID (gid://shopify/Collection/<id>).', 1;
    END;

    UPDATE dbo.ShippingPolicies
    SET ShopifyShippingCollectionGid = @NormalizedGid,
        DateModified = SYSUTCDATETIME()
    WHERE Id = @Id;
END;
GO

-- Verification: records and legacy profile IDs must remain unchanged.
SELECT
      Id
    , [Name]
    , ShopifyProfileId
    , ShopifyShippingCollectionGid
    , AllowsOnlineCheckout
FROM dbo.ShippingPolicies
ORDER BY [Name];
GO
