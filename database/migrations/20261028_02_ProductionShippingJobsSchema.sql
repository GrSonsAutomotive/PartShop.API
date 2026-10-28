/*
 Site_2024 — PRODUCTION shipping-job SCHEMA PREPARATION ONLY.
RUN MANUALLY AFTER REVIEW, AND ONLY ON DATABASE Site_2024.

Schema only: creates an empty queue table + safety indexes.
Does not change Parts, rates, existing shipping policies or Shopify.
Does not enable background worker, writes or any other feature flag.
Safe to re-run; never delete or reset existing queue rows.
*/
SET NOCOUNT ON;
IF DB_NAME() <> N'Site_2024'
    THROW 51210, 'PRODUCTION migration: expected exactly Site_2024.', 1;
IF OBJECT_ID(N'dbo.Parts',N'U') IS NULL
    THROW 51121, 'dbo.Parts is missing.', 1;
IF OBJECT_ID(N'dbo.ShippingPolicies',N'U') IS NULL OR
   COL_LENGTH(N'dbo.ShippingPolicies', N'ShopifyShippingCollectionGid') IS NULL
    THROW 51211, 'Install shipping collection mapping schema first.', 1;
IF OBJECT_ID(N'dbo.ShopifyShippingReassignmentJobs',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ShopifyShippingReassignmentJobs
    (
        JobId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ShippingReassignmentJobs PRIMARY KEY,
        PartId INT NOT NULL,
        ShopifyProductId BIGINT NOT NULL,
        FromPolicyId INT NOT NULL,
        ToPolicyId INT NOT NULL,
        RequestedByUserId INT NOT NULL,
        OriginalPublishedOnOnlineStore BIT NOT NULL,
        Status NVARCHAR(40) NOT NULL CONSTRAINT DF_ShippingJobs_Status DEFAULT N'Queued',
        IsOpen BIT NOT NULL CONSTRAINT DF_ShippingJobs_IsOpen DEFAULT (1),
        CheckCount INT NOT NULL CONSTRAINT DF_ShippingJobs_CheckCount DEFAULT (0),
        FailureCount INT NOT NULL CONSTRAINT DF_ShippingJobs_FailureCount DEFAULT (0),
        OldCollectionContainsProduct BIT NULL,
        NewCollectionContainsProduct BIT NULL,
        NextCheckUtc DATETIME2(3) NOT NULL CONSTRAINT DF_ShippingJobs_NextCheck DEFAULT SYSUTCDATETIME(),
        LeaseOwner UNIQUEIDENTIFIER NULL,
        LeaseUntilUtc DATETIME2(3) NULL,
        LastError NVARCHAR(1500) NULL,
        CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_ShippingJobs_Created DEFAULT SYSUTCDATETIME(),
        UpdatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_ShippingJobs_Updated DEFAULT SYSUTCDATETIME(),
        CompletedUtc DATETIME2(3) NULL
    );
END;
GO
IF DB_NAME() <> N'Site_2024'
    THROW 51212, 'Connected database changed; stop migration.', 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ShopifyShippingReassignmentJobs')
               AND name=N'UX_ShippingJobs_OneOpenPerPart')
    CREATE UNIQUE INDEX UX_ShippingJobs_OneOpenPerPart
    ON dbo.ShopifyShippingReassignmentJobs(PartId) WHERE IsOpen = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ShopifyShippingReassignmentJobs')
               AND name=N'IX_ShippingJobs_Claim')
    CREATE INDEX IX_ShippingJobs_Claim
    ON dbo.ShopifyShippingReassignmentJobs(IsOpen, NextCheckUtc, LeaseUntilUtc)
    INCLUDE (Status, CreatedUtc);
GO
IF DB_NAME() <> N'Site_2024'
    THROW 51213, 'Connected database changed; stop migration.', 1;
GO
SELECT COUNT(*) AS ExistingJobCount FROM dbo.ShopifyShippingReassignmentJobs;
SELECT TOP (10) JobId, PartId, FromPolicyId, ToPolicyId, Status, IsOpen, CreatedUtc
FROM dbo.ShopifyShippingReassignmentJobs ORDER BY CreatedUtc DESC;
GO