-- =============================================================================
-- Content Package Architecture — Forward Migration (Phase 1: Foundation)
-- =============================================================================
-- Adds ContentPackageId (uniqueidentifier NOT NULL DEFAULT NEWID()) to the 13
-- in-scope entities and seeds a dedicated "Content Package" system user for
-- cross-environment audit attribution.
--
-- This script is idempotent: re-running it will skip already-applied changes.
-- Paired rollback: sql-migration-content-package-architecture-rollback.sql
--
-- Run against: DXSuite_Director database
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- -----------------------------------------------------------------------------
-- 1. ContentPackageId columns on in-scope entities
-- -----------------------------------------------------------------------------
-- Pattern: ALTER TABLE ... ADD ContentPackageId uniqueidentifier NOT NULL
--          CONSTRAINT DF_{table}_ContentPackageId DEFAULT NEWID();
--          CREATE UNIQUE NONCLUSTERED INDEX UX_{table}_ContentPackageId ...

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblLocation') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblLocation
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblLocation_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblLocation_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblLocation'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblLocation_ContentPackageId ON dbo.tblLocation(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblLocationGroup') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblLocationGroup
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblLocationGroup_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblLocationGroup_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblLocationGroup'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblLocationGroup_ContentPackageId ON dbo.tblLocationGroup(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblDisplay') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblDisplay
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblDisplay_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblDisplay_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblDisplay'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblDisplay_ContentPackageId ON dbo.tblDisplay(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblDisplayGroup') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblDisplayGroup
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblDisplayGroup_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblDisplayGroup_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblDisplayGroup'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblDisplayGroup_ContentPackageId ON dbo.tblDisplayGroup(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAsset') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblAsset
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblAsset_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblAsset_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblAsset'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAsset_ContentPackageId ON dbo.tblAsset(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAssetGroup') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblAssetGroup
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblAssetGroup_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblAssetGroup_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblAssetGroup'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetGroup_ContentPackageId ON dbo.tblAssetGroup(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblScheduleEvent') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblScheduleEvent
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblScheduleEvent_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblScheduleEvent_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblScheduleEvent'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblScheduleEvent_ContentPackageId ON dbo.tblScheduleEvent(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblConstraint') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblConstraint
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblConstraint_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblConstraint_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblConstraint'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblConstraint_ContentPackageId ON dbo.tblConstraint(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAccessSchedule') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblAccessSchedule
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblAccessSchedule_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblAccessSchedule_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblAccessSchedule'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAccessSchedule_ContentPackageId ON dbo.tblAccessSchedule(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblLocationAccessRule') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblLocationAccessRule
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblLocationAccessRule_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblLocationAccessRule_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblLocationAccessRule'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblLocationAccessRule_ContentPackageId ON dbo.tblLocationAccessRule(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblFloorPlanElement') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblFloorPlanElement
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblFloorPlanElement_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblFloorPlanElement_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblFloorPlanElement'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblFloorPlanElement_ContentPackageId ON dbo.tblFloorPlanElement(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblApprovalChain') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblApprovalChain
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblApprovalChain_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblApprovalChain_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblApprovalChain'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblApprovalChain_ContentPackageId ON dbo.tblApprovalChain(ContentPackageId);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAssetContentMetadata') AND name = 'ContentPackageId')
BEGIN
    ALTER TABLE dbo.tblAssetContentMetadata
        ADD ContentPackageId uniqueidentifier NOT NULL CONSTRAINT DF_tblAssetContentMetadata_ContentPackageId DEFAULT NEWID();
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblAssetContentMetadata_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblAssetContentMetadata'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetContentMetadata_ContentPackageId ON dbo.tblAssetContentMetadata(ContentPackageId);

-- -----------------------------------------------------------------------------
-- 1b. Content package history tables
-- -----------------------------------------------------------------------------
-- Audit log for every export/import/push/receive operation. Written by the
-- ContentPackageBundleBuilder / ContentPackageBundleImporter services so
-- operators can trace which ContentPackageIds moved between environments and
-- when.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblContentPackageHistory' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.tblContentPackageHistory (
        ContentPackageHistoryId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblContentPackageHistory PRIMARY KEY,
        Direction varchar(20) NOT NULL,
        Status varchar(20) NOT NULL,
        BundleFileName nvarchar(255) NULL,
        InitiatedByUser nvarchar(255) NULL,
        ExportedByUser nvarchar(255) NULL,
        SourceEnvironment nvarchar(100) NULL,
        ScopeDescription nvarchar(500) NULL,
        TotalRecords int NOT NULL CONSTRAINT DF_tblContentPackageHistory_TotalRecords DEFAULT 0,
        InsertedCount int NOT NULL CONSTRAINT DF_tblContentPackageHistory_InsertedCount DEFAULT 0,
        UpdatedCount int NOT NULL CONSTRAINT DF_tblContentPackageHistory_UpdatedCount DEFAULT 0,
        SkippedCount int NOT NULL CONSTRAINT DF_tblContentPackageHistory_SkippedCount DEFAULT 0,
        StartedAt datetime NOT NULL,
        CompletedAt datetime NULL,
        ErrorMessage nvarchar(2000) NULL
    );
    CREATE NONCLUSTERED INDEX IX_tblContentPackageHistory_StartedAt ON dbo.tblContentPackageHistory(StartedAt);
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblContentPackageHistoryItem' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.tblContentPackageHistoryItem (
        ContentPackageHistoryItemId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblContentPackageHistoryItem PRIMARY KEY,
        ContentPackageHistoryId int NOT NULL,
        ContentPackageId uniqueidentifier NOT NULL,
        EntityType varchar(50) NOT NULL,
        Action varchar(20) NOT NULL,
        SourceUserPrincipalName nvarchar(255) NULL,
        Notes nvarchar(500) NULL,
        CONSTRAINT FK_tblContentPackageHistoryItem_History FOREIGN KEY (ContentPackageHistoryId)
            REFERENCES dbo.tblContentPackageHistory(ContentPackageHistoryId) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX IX_tblContentPackageHistoryItem_HistoryId ON dbo.tblContentPackageHistoryItem(ContentPackageHistoryId);
    CREATE NONCLUSTERED INDEX IX_tblContentPackageHistoryItem_ContentPackageId ON dbo.tblContentPackageHistoryItem(ContentPackageId);
END

-- -----------------------------------------------------------------------------
-- 1c. Content package environment labels (configurable source environment dropdown)
-- -----------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblContentPackageEnvironmentLabel' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.tblContentPackageEnvironmentLabel (
        ContentPackageEnvironmentLabelId int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_tblContentPackageEnvironmentLabel PRIMARY KEY,
        Label nvarchar(100) NOT NULL,
        SortOrder int NOT NULL CONSTRAINT DF_CpkgEnvLabel_SortOrder DEFAULT 0,
        CreateDate datetime NOT NULL CONSTRAINT DF_CpkgEnvLabel_CreateDate DEFAULT GETDATE(),
        CreatedByUser nvarchar(255) NULL
    );
END

-- -----------------------------------------------------------------------------
-- 2. Seed Content Package system user
-- -----------------------------------------------------------------------------
-- Used as the fallback user attribution target when an imported record's source
-- user cannot be matched by UserPrincipalName in the target environment.
-- Role is looked up by name; falls back to the lowest RoleId if no match.

IF NOT EXISTS (SELECT 1 FROM dbo.tblUser WHERE UserPrincipalName = 'contentpackage@dxsuite.system')
BEGIN
    DECLARE @ContentPackageRoleId int;
    SELECT TOP 1 @ContentPackageRoleId = RoleId FROM dbo.tblUserRole ORDER BY RoleId ASC;

    IF @ContentPackageRoleId IS NULL
    BEGIN
        RAISERROR('Cannot seed Content Package system user: tblUserRole is empty.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.tblUser (FriendlyName, GivenName, Surname, UserPrincipalName, Title, RoleId, PulseAccess, IsDisabled, CreateDate)
    VALUES ('Content Package System', 'Content Package', 'System', 'contentpackage@dxsuite.system', 'System Account', @ContentPackageRoleId, 0, 1, GETDATE());
END

COMMIT TRANSACTION;

PRINT 'Content Package architecture Phase 1 migration applied successfully.';
