/*
Site_2024 — PRODUCTION shipping collections SCHEMA PREPARATION ONLY
RUN MANUALLY AFTER REVIEW, AND ONLY ON DATABASE Site_2024.

This migration:
- Adds a nullable mapping column to existing shipping policies.
- Preserves ShopifyProfileId and every existing policy/product/part record.
- Makes the mapped collection readable by the new API.
- Does not create collections, change rates, apply tags, queue jobs, or enable workers.

Requires a verified backup and a read-only baseline first.
*/
SET NOCOUNT ON;
IF DB_NAME() <> N'Site_2024'
    THROW 51200, 'PRODUCTION migration: expected exactly Site_2024.', 1;

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

IF DB_NAME() <> N'Site_2024'
    THROW 51201, 'Connected database changed; stop migration.', 1;
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

IF DB_NAME() <> N'Site_2024'
    THROW 51202, 'Connected database changed; stop migration.', 1;
GO

-- This mapping procedure does not change actual Shopify collections.
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

IF DB_NAME() <> N'Site_2024'
    THROW 51203, 'Connected database changed; stop migration.', 1;
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
