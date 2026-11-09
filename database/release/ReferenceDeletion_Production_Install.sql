-- SQLCMD MODE ONLY. Run from the API repository root after explicit operator approval.
-- No grant, shipping migration, seed data, feature flag, or Shopify operation is included.
:on error exit
:setvar ReferenceDeletionApproved "NO"
IF DB_NAME() <> N'Site_2024'
    THROW 50540, 'Expected production database Site_2024. Stop.', 1;
IF N'$(ReferenceDeletionApproved)'<>N'YES'
    THROW 50541, 'Installation is on HOLD. Explicit approval and a reviewed operator copy are required.', 1;
IF @@TRANCOUNT<>0
    THROW 50542, 'Start a new dedicated installation session.', 1;
GO
:r .\database\release\ReferenceDeletion_Production_Preflight_READ_ONLY.sql
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO
:r .\database\migrations\20261107_01_ReferenceDataSafeDeletion.sql
GO
:r .\database\release\ReferenceDeletion_Production_PostInstall_READ_ONLY.sql
GO
IF DB_NAME()<>N'Site_2024' OR XACT_STATE()<>1 OR @@TRANCOUNT<>1
    THROW 50543, 'Installation transaction is not committable. Disconnect and review.', 1;
COMMIT TRANSACTION;
PRINT N'All six modules installed. API-identity EXECUTE verification remains required.';
GO
-- :on error exit closes the sqlcmd connection on failure. SQL Server rolls back
-- any remaining uncommitted transaction when that dedicated connection closes.
-- For an SSMS SQLCMD run that aborts with the connection open, explicitly ROLLBACK
-- or disconnect before retrying. Never continue after an installation error.
