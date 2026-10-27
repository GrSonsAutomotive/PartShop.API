/*
 Site_2024 — DEV PILOT ONLY. Run manually against Site_2024_Dev.
 No changes to Parts, ShippingPolicies, Shopify or production are made here.
 Prerequisite before enabling AllowMarketDrivenShippingAsyncWorker.

 Durable queue with one OPEN job per part. AttentionRequired keeps IsOpen=1
 so no new reassignment can silently override an interrupted operation.
 Safe to re-run. Does not delete or rewrite existing records.
*/
SET NOCOUNT ON;
IF DB_NAME() NOT LIKE '%Dev%'
    THROW 51120, 'DEV-only migration: not connected to a DEV database.', 1;
IF OBJECT_ID(N'dbo.Parts',N'U') IS NULL
    THROW 51121, 'dbo.Parts is missing.', 1;
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
SELECT TOP (10) JobId, PartId, FromPolicyId, ToPolicyId, Status, IsOpen, CreatedUtc
FROM dbo.ShopifyShippingReassignmentJobs ORDER BY CreatedUtc DESC;
GO