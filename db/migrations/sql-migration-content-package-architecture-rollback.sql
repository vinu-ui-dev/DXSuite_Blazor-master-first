-- =============================================================================
-- Content Package Architecture — ROLLBACK Migration (Phase 1: Foundation)
-- =============================================================================
-- Reverses sql-migration-content-package-architecture.sql. Kept in sync with
-- the forward script throughout implementation — any change to the forward
-- script must have a corresponding inverse here.
--
-- WARNING:
--   * Rollback removes the ContentPackageId columns, their unique indexes, and
--     the seeded Content Package system user.
--   * Content that was previously exported/imported between environments is
--     NOT reverted — only the cross-environment identity tracking is removed.
--     Migrated assets, displays, schedules, etc. remain in place.
--   * The tblContentPackageHistory/tblContentPackageHistoryItem/
--     tblContentPackageEnvironmentLabel tables are dropped here as well.
--
-- This script is idempotent: re-running it will skip already-rolled-back
-- changes.
--
-- Run against: DXSuite_Director database
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- -----------------------------------------------------------------------------
-- 0. Drop content package history + environment label tables
-- -----------------------------------------------------------------------------
-- Drop child table first to avoid FK violations. Cascading delete on the FK
-- would clean up history items automatically, but the explicit drop order
-- keeps this script parallel to the forward migration.

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblContentPackageEnvironmentLabel' AND schema_id = SCHEMA_ID('dbo'))
    DROP TABLE dbo.tblContentPackageEnvironmentLabel;

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblContentPackageHistoryItem' AND schema_id = SCHEMA_ID('dbo'))
    DROP TABLE dbo.tblContentPackageHistoryItem;

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblContentPackageHistory' AND schema_id = SCHEMA_ID('dbo'))
    DROP TABLE dbo.tblContentPackageHistory;

-- -----------------------------------------------------------------------------
-- 1. Remove seeded Content Package system user
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.tblUser WHERE UserPrincipalName = 'contentpackage@dxsuite.system')
BEGIN
    DELETE FROM dbo.tblUser WHERE UserPrincipalName = 'contentpackage@dxsuite.system';
END

-- -----------------------------------------------------------------------------
-- 2. Drop unique indexes on ContentPackageId columns
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblLocation_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblLocation'))
    DROP INDEX UX_tblLocation_ContentPackageId ON dbo.tblLocation;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblLocationGroup_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblLocationGroup'))
    DROP INDEX UX_tblLocationGroup_ContentPackageId ON dbo.tblLocationGroup;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblDisplay_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblDisplay'))
    DROP INDEX UX_tblDisplay_ContentPackageId ON dbo.tblDisplay;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblDisplayGroup_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblDisplayGroup'))
    DROP INDEX UX_tblDisplayGroup_ContentPackageId ON dbo.tblDisplayGroup;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblAsset_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblAsset'))
    DROP INDEX UX_tblAsset_ContentPackageId ON dbo.tblAsset;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblAssetGroup_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblAssetGroup'))
    DROP INDEX UX_tblAssetGroup_ContentPackageId ON dbo.tblAssetGroup;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblScheduleEvent_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblScheduleEvent'))
    DROP INDEX UX_tblScheduleEvent_ContentPackageId ON dbo.tblScheduleEvent;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblConstraint_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblConstraint'))
    DROP INDEX UX_tblConstraint_ContentPackageId ON dbo.tblConstraint;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblAccessSchedule_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblAccessSchedule'))
    DROP INDEX UX_tblAccessSchedule_ContentPackageId ON dbo.tblAccessSchedule;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblLocationAccessRule_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblLocationAccessRule'))
    DROP INDEX UX_tblLocationAccessRule_ContentPackageId ON dbo.tblLocationAccessRule;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblFloorPlanElement_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblFloorPlanElement'))
    DROP INDEX UX_tblFloorPlanElement_ContentPackageId ON dbo.tblFloorPlanElement;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblApprovalChain_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblApprovalChain'))
    DROP INDEX UX_tblApprovalChain_ContentPackageId ON dbo.tblApprovalChain;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblAssetContentMetadata_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblAssetContentMetadata'))
    DROP INDEX UX_tblAssetContentMetadata_ContentPackageId ON dbo.tblAssetContentMetadata;

-- -----------------------------------------------------------------------------
-- 3. Drop default constraints on ContentPackageId columns
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblLocation_ContentPackageId')
    ALTER TABLE dbo.tblLocation DROP CONSTRAINT DF_tblLocation_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblLocationGroup_ContentPackageId')
    ALTER TABLE dbo.tblLocationGroup DROP CONSTRAINT DF_tblLocationGroup_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblDisplay_ContentPackageId')
    ALTER TABLE dbo.tblDisplay DROP CONSTRAINT DF_tblDisplay_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblDisplayGroup_ContentPackageId')
    ALTER TABLE dbo.tblDisplayGroup DROP CONSTRAINT DF_tblDisplayGroup_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblAsset_ContentPackageId')
    ALTER TABLE dbo.tblAsset DROP CONSTRAINT DF_tblAsset_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblAssetGroup_ContentPackageId')
    ALTER TABLE dbo.tblAssetGroup DROP CONSTRAINT DF_tblAssetGroup_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblScheduleEvent_ContentPackageId')
    ALTER TABLE dbo.tblScheduleEvent DROP CONSTRAINT DF_tblScheduleEvent_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblConstraint_ContentPackageId')
    ALTER TABLE dbo.tblConstraint DROP CONSTRAINT DF_tblConstraint_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblAccessSchedule_ContentPackageId')
    ALTER TABLE dbo.tblAccessSchedule DROP CONSTRAINT DF_tblAccessSchedule_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblLocationAccessRule_ContentPackageId')
    ALTER TABLE dbo.tblLocationAccessRule DROP CONSTRAINT DF_tblLocationAccessRule_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblFloorPlanElement_ContentPackageId')
    ALTER TABLE dbo.tblFloorPlanElement DROP CONSTRAINT DF_tblFloorPlanElement_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblApprovalChain_ContentPackageId')
    ALTER TABLE dbo.tblApprovalChain DROP CONSTRAINT DF_tblApprovalChain_ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblAssetContentMetadata_ContentPackageId')
    ALTER TABLE dbo.tblAssetContentMetadata DROP CONSTRAINT DF_tblAssetContentMetadata_ContentPackageId;

-- -----------------------------------------------------------------------------
-- 4. Drop ContentPackageId columns
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblLocation') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblLocation DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblLocationGroup') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblLocationGroup DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblDisplay') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblDisplay DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblDisplayGroup') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblDisplayGroup DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAsset') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblAsset DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAssetGroup') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblAssetGroup DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblScheduleEvent') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblScheduleEvent DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblConstraint') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblConstraint DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAccessSchedule') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblAccessSchedule DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblLocationAccessRule') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblLocationAccessRule DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblFloorPlanElement') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblFloorPlanElement DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblApprovalChain') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblApprovalChain DROP COLUMN ContentPackageId;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAssetContentMetadata') AND name = 'ContentPackageId')
    ALTER TABLE dbo.tblAssetContentMetadata DROP COLUMN ContentPackageId;

COMMIT TRANSACTION;

PRINT 'Content Package architecture Phase 1 rollback applied successfully.';
