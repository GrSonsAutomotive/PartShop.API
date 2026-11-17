-- Inventory Cleanup (DEV first). Reviewed against 2026-11-17 Site_2024 schema.
-- Requires DBA review and execution in the DEV Azure SQL database before API testing.
-- Never run against production until DEV validation and approval.
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'dbo.InventoryCleanupDeletedParts', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.InventoryCleanupDeletedParts
  (
    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PartId int NOT NULL,
    PartName nvarchar(128) NOT NULL,
    DeletedByUserId int NOT NULL,
    DeletedAtUtc datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    PartSnapshotJson nvarchar(max) NOT NULL,
    AuditRowCount int NOT NULL,
    PhotoQueueCount int NOT NULL
  );
  CREATE INDEX IX_InventoryCleanupDeletedParts_PartId ON dbo.InventoryCleanupDeletedParts (PartId);
END;
GO
IF OBJECT_ID(N'dbo.InventoryCleanupAuditArchive', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.InventoryCleanupAuditArchive
  (
    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PartId int NOT NULL,
    OriginalAuditId int NOT NULL,
    ChangedByUserId int NOT NULL,
    ChangeType nvarchar(50) NOT NULL,
    ColumnName nvarchar(128) NOT NULL,
    OldValue nvarchar(4000) NULL,
    NewValue nvarchar(4000) NULL,
    ChangedOn datetime NOT NULL,
    ArchivedAtUtc datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME()
  );
  CREATE INDEX IX_InventoryCleanupAuditArchive_PartId ON dbo.InventoryCleanupAuditArchive (PartId);
END;
GO
IF OBJECT_ID(N'dbo.InventoryCleanupPhotoQueue', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.InventoryCleanupPhotoQueue
  (
    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PartId int NOT NULL,
    Url nvarchar(260) NOT NULL,
    Status nvarchar(20) NOT NULL DEFAULT N'Pending',
    Attempts int NOT NULL DEFAULT 0,
    LastError nvarchar(2000) NULL,
    DateCreatedUtc datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    DateModifiedUtc datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME()
  );
  CREATE INDEX IX_InventoryCleanupPhotoQueue_Status ON dbo.InventoryCleanupPhotoQueue (Status, Id);
END;
GO
CREATE OR ALTER PROCEDURE dbo.InventoryCleanup_Delete
    @PartId int,
    @DeletedByUserId int,
    @MinimumAgeDays int = 90
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @PartId <= 0 OR @DeletedByUserId <= 0
        THROW 51001, 'Invalid part or administrator.', 1;
    IF @MinimumAgeDays < 90
        SET @MinimumAgeDays = 90;

    DECLARE @PartName nvarchar(128), @PartSnapshot nvarchar(max),
            @Status nvarchar(128), @LastModified datetime,
            @ProductId bigint, @VariantId bigint, @InventoryId bigint,
            @AuditRowCount int, @PhotoQueueCount int;

    BEGIN TRY
        BEGIN TRANSACTION;
        SELECT @PartName=p.name, @LastModified=p.datemodified,
               @ProductId=p.ShopifyProductId, @VariantId=p.ShopifyVariantId,
               @InventoryId=p.ShopifyInventoryItemId, @Status=a.status
        FROM dbo.Parts p WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN dbo.Available a ON a.id=p.availableId
        WHERE p.id=@PartId;

        IF @PartName IS NULL
            THROW 51002, 'Part no longer exists.', 1;
        IF LOWER(LTRIM(RTRIM(@Status))) <> N'unavailable'
            THROW 51003, 'Only unavailable parts may be permanently deleted.', 1;
        IF @LastModified IS NULL OR @LastModified > DATEADD(DAY, -@MinimumAgeDays, SYSUTCDATETIME())
            THROW 51004, 'Part has not satisfied the minimum age based on its last modification.', 1;
        IF @ProductId IS NOT NULL OR @VariantId IS NOT NULL OR @InventoryId IS NOT NULL
            THROW 51005, 'A Shopify link remains; coordinated Shopify deletion is required.', 1;

        -- Never cascade away sales or compromise order / refund records.
        IF EXISTS (SELECT 1 FROM dbo.PartSales WHERE PartId=@PartId)
          OR EXISTS (SELECT 1 FROM dbo.RefundRequests WHERE PartId=@PartId)
          OR EXISTS (SELECT 1 FROM dbo.RefundRequestItems WHERE PartId=@PartId)
          OR EXISTS (SELECT 1 FROM dbo.RefundFinalizationItems WHERE PartId=@PartId)
          OR EXISTS (SELECT 1 FROM dbo.RefundInventoryDispositionActions WHERE PartId=@PartId)
          OR EXISTS (SELECT 1 FROM dbo.RefundInventoryDispositionItems WHERE PartId=@PartId)
            THROW 51006, 'This part has sales or refund history. Preserve it instead of permanently deleting.', 1;

        IF EXISTS (SELECT 1 FROM dbo.AdminDiscountCodes WHERE PartId=@PartId)
            THROW 51007, 'This part is referenced by a discount. Resolve the discount before deleting.', 1;

        SELECT @PartSnapshot = (
            SELECT * FROM dbo.Parts WHERE id=@PartId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        );

        -- Preserve full edit history before removing the audited listing.
        INSERT dbo.InventoryCleanupAuditArchive
          (PartId,OriginalAuditId,ChangedByUserId,ChangeType,ColumnName,OldValue,NewValue,ChangedOn)
        SELECT PartId,Id,ChangedByUserId,ChangeType,ColumnName,OldValue,NewValue,ChangedOn
        FROM dbo.PartsAudit WHERE PartId=@PartId;
        SET @AuditRowCount=@@ROWCOUNT;

        -- Queue only /uploads/items/ files, and never queue a file still referenced elsewhere.
        ;WITH Photos AS (
          SELECT [Url] FROM dbo.PartImages WHERE PartId=@PartId
          UNION
          SELECT [image] FROM dbo.Parts WHERE id=@PartId AND [image] IS NOT NULL
        )
        INSERT dbo.InventoryCleanupPhotoQueue (PartId, Url)
        SELECT @PartId, photos.[Url]
        FROM Photos photos
        WHERE photos.[Url] LIKE N'/uploads/items/%'
          AND NOT EXISTS (SELECT 1 FROM dbo.PartImages pi
                          WHERE pi.PartId<>@PartId AND pi.[Url]=photos.[Url])
          AND NOT EXISTS (SELECT 1 FROM dbo.Parts p
                          WHERE p.id<>@PartId AND p.[image]=photos.[Url])
          AND NOT EXISTS (SELECT 1 FROM dbo.InventoryCleanupPhotoQueue q
                          WHERE q.PartId=@PartId AND q.[Url]=photos.[Url]);
        SET @PhotoQueueCount=@@ROWCOUNT;

        DELETE FROM dbo.PartCategories WHERE PartId=@PartId;
        DELETE FROM dbo.PartFitments WHERE PartId=@PartId;
        DELETE FROM dbo.PartImages WHERE PartId=@PartId;
        DELETE FROM dbo.PartsAudit WHERE PartId=@PartId;
        DELETE FROM dbo.Parts WHERE id=@PartId;

        INSERT dbo.InventoryCleanupDeletedParts
          (PartId,PartName,DeletedByUserId,PartSnapshotJson,AuditRowCount,PhotoQueueCount)
        VALUES (@PartId,@PartName,@DeletedByUserId,@PartSnapshot,@AuditRowCount,@PhotoQueueCount);
        COMMIT TRANSACTION;
        SELECT @PhotoQueueCount AS PhotoQueueCount, @AuditRowCount AS ArchivedAuditCount;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
-- Post-installation verification (read-only):
-- SELECT OBJECT_ID(N'dbo.InventoryCleanup_Delete',N'P') AS ProcedureId;
-- SELECT TOP (5) * FROM dbo.InventoryCleanupPhotoQueue ORDER BY Id DESC;
