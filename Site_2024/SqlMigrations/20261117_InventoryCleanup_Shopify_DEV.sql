-- Apply to isolated DEV Azure SQL only, AFTER 20261117_InventoryCleanup_DEV.sql.
-- No production SQL changes or Shopify calls are performed by this script.
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'dbo.InventoryCleanupShopifyOperations',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryCleanupShopifyOperations
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryCleanupShopifyOperations PRIMARY KEY,
        PartId int NOT NULL,
        ShopifyProductId bigint NOT NULL,
        ShopDomain nvarchar(255) NOT NULL,
        State nvarchar(32) NOT NULL, -- Prepared, Attempted, ShopifyDeleted, Completed, Cancelled
        RequestedByUserId int NOT NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_InventoryCleanupShopifyOperations_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_InventoryCleanupShopifyOperations_Updated DEFAULT SYSUTCDATETIME(),
        AttemptCount int NOT NULL CONSTRAINT DF_InventoryCleanupShopifyOperations_Attempts DEFAULT 0,
        LastAttemptAtUtc datetime2(0) NULL,
        ShopifyDeletedAtUtc datetime2(0) NULL,
        LocalDeletedAtUtc datetime2(0) NULL,
        LastError nvarchar(2000) NULL,
        CONSTRAINT UQ_InventoryCleanupShopifyOperations_PartId UNIQUE (PartId),
        CONSTRAINT CK_InventoryCleanupShopifyOperations_State
          CHECK (State IN (N'Prepared',N'Attempted',N'ShopifyDeleted',N'Completed',N'Cancelled'))
    );
END;
GO

-- Prevent normal API parts updates after an external deletion operation begins.
-- The finalizing stored procedure alone may temporarily lift the protection.
CREATE OR ALTER TRIGGER dbo.InventoryCleanupShopify_BlockPartUpdate
ON dbo.Parts
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF CONVERT(int, ISNULL(SESSION_CONTEXT(N'InventoryCleanupShopifyFinalizing'),0)) = 1 RETURN;
    IF EXISTS
    (
        SELECT 1 FROM inserted i
        INNER JOIN dbo.InventoryCleanupShopifyOperations o ON o.PartId=i.id
        WHERE o.State IN (N'Prepared', N'Attempted', N'ShopifyDeleted')
    )
      THROW 51100, 'Part is locked for coordinated Shopify cleanup. Finish or recover that operation first.', 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.InventoryCleanupShopify_Begin
    @PartId int, @UserId int, @ShopDomain nvarchar(255),
    @ShopifyProductId bigint
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @PartId<=0 OR @UserId<=0 OR @ShopifyProductId<=0
        THROW 51101, 'Invalid cleanup request.', 1;
    IF NULLIF(LTRIM(RTRIM(@ShopDomain)),N'') IS NULL
        THROW 51102, 'Shopify store identity is required.', 1;

    BEGIN TRY
      BEGIN TRANSACTION;
      DECLARE @status nvarchar(128), @since datetime2(0),
              @product bigint, @variant bigint, @inventory bigint,
              @sold bigint, @lastOrder bigint, @soldOn datetime2(0),
              @found bit=0;
      SELECT @status=a.status, @since=p.UnavailableSinceUtc,
             @product=p.ShopifyProductId, @variant=p.ShopifyVariantId,
             @inventory=p.ShopifyInventoryItemId, @sold=ps.QuantitySold,
             @lastOrder=p.ShopifyOrderId, @soldOn=p.SoldOnUtc, @found=1
      FROM dbo.Parts p WITH (UPDLOCK,HOLDLOCK)
      INNER JOIN dbo.Available a ON a.id=p.availableId
      OUTER APPLY (SELECT SUM(CONVERT(bigint,QuantitySold)) QuantitySold
                   FROM dbo.PartSales WHERE PartId=p.id) ps
      WHERE p.id=@PartId;

      IF @found=0 OR @product IS NULL OR @product<>@ShopifyProductId
         THROW 51103, 'Part missing or Shopify product linkage changed.', 1;
      IF LOWER(LTRIM(RTRIM(@status)))<>N'unavailable'
         THROW 51104, 'Part must be unavailable.', 1;
      IF @since IS NULL OR @since>DATEADD(DAY,-90,SYSUTCDATETIME())
         THROW 51105, 'Part has not been unavailable for 90 days.', 1;
      IF ISNULL(@sold,0)>0 OR @lastOrder IS NOT NULL OR @soldOn IS NOT NULL
         THROW 51106, 'Sales history requires archival, not deletion.', 1;

      IF EXISTS(SELECT 1 FROM dbo.PartSales WHERE PartId=@PartId)
        OR EXISTS(SELECT 1 FROM dbo.RefundRequests WHERE PartId=@PartId)
        OR EXISTS(SELECT 1 FROM dbo.RefundRequestItems WHERE PartId=@PartId)
        OR EXISTS(SELECT 1 FROM dbo.RefundFinalizationItems WHERE PartId=@PartId)
        OR EXISTS(SELECT 1 FROM dbo.RefundInventoryDispositionActions WHERE PartId=@PartId)
        OR EXISTS(SELECT 1 FROM dbo.RefundInventoryDispositionItems WHERE PartId=@PartId)
        OR EXISTS(SELECT 1 FROM dbo.AdminDiscountCodes WHERE PartId=@PartId)
         THROW 51107, 'Sales, return, or discount dependency prevents deletion.', 1;

      IF EXISTS(SELECT 1 FROM dbo.Parts WHERE id<>@PartId AND ShopifyProductId=@ShopifyProductId)
         THROW 51108, 'Another listing references this Shopify product.', 1;

      IF EXISTS(SELECT 1 FROM dbo.InventoryCleanupShopifyOperations WHERE PartId=@PartId)
      BEGIN
         IF EXISTS(SELECT 1 FROM dbo.InventoryCleanupShopifyOperations
                       WHERE PartId=@PartId AND State=N'Cancelled')
         BEGIN
            UPDATE dbo.InventoryCleanupShopifyOperations
            SET ShopifyProductId=@ShopifyProductId, ShopDomain=@ShopDomain,
                RequestedByUserId=@UserId, State=N'Prepared',
                AttemptCount=0, LastAttemptAtUtc=NULL, ShopifyDeletedAtUtc=NULL,
                LocalDeletedAtUtc=NULL, LastError=NULL, UpdatedAtUtc=SYSUTCDATETIME()
            WHERE PartId=@PartId;
         END
         ELSE IF NOT EXISTS(SELECT 1 FROM dbo.InventoryCleanupShopifyOperations
                       WHERE PartId=@PartId AND ShopifyProductId=@ShopifyProductId
                         AND ShopDomain=@ShopDomain
                         AND State IN (N'Prepared',N'Attempted',N'ShopifyDeleted'))
            THROW 51109, 'An incompatible or finished cleanup operation exists.', 1;
      END
      ELSE
      BEGIN
        INSERT dbo.InventoryCleanupShopifyOperations
            (PartId,ShopifyProductId,ShopDomain,State,RequestedByUserId)
        VALUES (@PartId,@ShopifyProductId,@ShopDomain,N'Prepared',@UserId);
      END
      COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
      IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
      THROW;
    END CATCH
    SELECT Id, PartId, ShopifyProductId, ShopDomain, State, AttemptCount
    FROM dbo.InventoryCleanupShopifyOperations WHERE PartId=@PartId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.InventoryCleanupShopify_CancelPrepared
    @PartId int, @ShopDomain nvarchar(255), @ShopifyProductId bigint
AS
BEGIN
    SET NOCOUNT ON;
    -- Safe only if NO mutation attempt has ever started.
    UPDATE dbo.InventoryCleanupShopifyOperations
    SET State=N'Cancelled', UpdatedAtUtc=SYSUTCDATETIME()
    WHERE PartId=@PartId AND ShopDomain=@ShopDomain
      AND ShopifyProductId=@ShopifyProductId AND State=N'Prepared'
      AND AttemptCount=0;
END;
GO

CREATE OR ALTER PROCEDURE dbo.InventoryCleanupShopify_MarkAttempt
    @PartId int, @ShopDomain nvarchar(255), @ShopifyProductId bigint
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.InventoryCleanupShopifyOperations
    SET State=N'Attempted', AttemptCount=AttemptCount+1,
        LastAttemptAtUtc=SYSUTCDATETIME(), UpdatedAtUtc=SYSUTCDATETIME(),
        LastError=NULL
    WHERE PartId=@PartId AND ShopDomain=@ShopDomain
      AND ShopifyProductId=@ShopifyProductId AND State IN (N'Prepared',N'Attempted');
    IF @@ROWCOUNT<>1 THROW 51110, 'Shopify operation is not eligible for an attempt.', 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.InventoryCleanupShopify_MarkDeleted
    @PartId int, @ShopDomain nvarchar(255), @ShopifyProductId bigint
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.InventoryCleanupShopifyOperations
    SET State=N'ShopifyDeleted',
        ShopifyDeletedAtUtc=COALESCE(ShopifyDeletedAtUtc,SYSUTCDATETIME()),
        UpdatedAtUtc=SYSUTCDATETIME(), LastError=NULL
    WHERE PartId=@PartId AND ShopDomain=@ShopDomain AND ShopifyProductId=@ShopifyProductId
      AND State IN (N'Prepared',N'Attempted',N'ShopifyDeleted');
    IF @@ROWCOUNT<>1 THROW 51111, 'Shopify deletion acknowledgement did not match the operation.', 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.InventoryCleanupShopify_RecordError
    @PartId int, @ShopDomain nvarchar(255), @Message nvarchar(2000)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.InventoryCleanupShopifyOperations
    SET LastError=LEFT(@Message,2000), UpdatedAtUtc=SYSUTCDATETIME()
    WHERE PartId=@PartId AND ShopDomain=@ShopDomain
      AND State IN (N'Prepared',N'Attempted',N'ShopifyDeleted');
END;
GO

-- This procedure is the ONLY path permitted to unlink a Shopify product before
-- the original local hard-delete procedure. Both happen in the SAME SQL transaction.
CREATE OR ALTER PROCEDURE dbo.InventoryCleanupShopify_Finalize
    @PartId int, @UserId int, @ShopDomain nvarchar(255), @ShopifyProductId bigint
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
      BEGIN TRANSACTION;
      DECLARE @found int=0;
      SELECT @found=1 FROM dbo.InventoryCleanupShopifyOperations WITH(UPDLOCK,HOLDLOCK)
      WHERE PartId=@PartId AND ShopifyProductId=@ShopifyProductId
        AND ShopDomain=@ShopDomain AND State=N'ShopifyDeleted';
      IF @found=0 THROW 51112, 'Shopify product deletion has not been confirmed.', 1;

      IF NOT EXISTS(SELECT 1 FROM dbo.Parts WITH(UPDLOCK,HOLDLOCK)
                    WHERE id=@PartId AND ShopifyProductId=@ShopifyProductId)
        THROW 51113, 'Local Shopify link changed since external deletion.', 1;

      -- These checks must precede unlinking. InventoryCleanup_Delete rechecks
      -- availability, age, dependencies and the original empty Shopify link.
      IF EXISTS(SELECT 1 FROM dbo.Parts WHERE id=@PartId
                AND (ShopifyOrderId IS NOT NULL OR SoldOnUtc IS NOT NULL))
          OR EXISTS(SELECT 1 FROM dbo.PartSales WHERE PartId=@PartId)
        THROW 51114, 'Sales history appeared during deletion.', 1;

      EXEC sys.sp_set_session_context @key=N'InventoryCleanupShopifyFinalizing',@value=1;
      UPDATE dbo.Parts
        SET ShopifyProductId=NULL, ShopifyVariantId=NULL, ShopifyInventoryItemId=NULL
        WHERE id=@PartId AND ShopifyProductId=@ShopifyProductId;
      IF @@ROWCOUNT<>1 THROW 51115, 'Local Shopify unlink failed.', 1;

      EXEC dbo.InventoryCleanup_Delete
           @PartId=@PartId, @DeletedByUserId=@UserId, @MinimumAgeDays=90;

      UPDATE dbo.InventoryCleanupShopifyOperations
         SET State=N'Completed', LocalDeletedAtUtc=SYSUTCDATETIME(),
             UpdatedAtUtc=SYSUTCDATETIME(), LastError=NULL
      WHERE PartId=@PartId AND State=N'ShopifyDeleted';
      IF @@ROWCOUNT<>1 THROW 51116, 'Unable to mark cleanup as finished.', 1;

      COMMIT TRANSACTION;
      EXEC sys.sp_set_session_context @key=N'InventoryCleanupShopifyFinalizing',@value=NULL;
    END TRY
    BEGIN CATCH
      IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
      EXEC sys.sp_set_session_context @key=N'InventoryCleanupShopifyFinalizing',@value=NULL;
      THROW;
    END CATCH
END;
GO

-- Read-only verification:
-- SELECT name FROM sys.procedures WHERE name LIKE N'InventoryCleanupShopify_%';
-- SELECT TOP(10) * FROM dbo.InventoryCleanupShopifyOperations ORDER BY Id DESC;
