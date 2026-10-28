-- ============================================================
-- DXSuite Migration Patch 4: SLC_DirectorDEV
-- Adds missing columns to existing tables identified from
-- startup log errors. All operations are idempotent.
-- ============================================================

USE SLC_DirectorDEV;
GO

-- tblAsset: ContentPackageId
-- Guid, non-nullable, auto-assigned per existing row
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.tblAsset') AND name = N'ContentPackageId'
)
BEGIN
    ALTER TABLE dbo.tblAsset ADD ContentPackageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    PRINT 'Added tblAsset.ContentPackageId';
END
GO

-- tblAsset: ContentSourceUrl
-- varchar(500), nullable
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.tblAsset') AND name = N'ContentSourceUrl'
)
BEGIN
    ALTER TABLE dbo.tblAsset ADD ContentSourceUrl VARCHAR(500) NULL;
    PRINT 'Added tblAsset.ContentSourceUrl';
END
GO

-- tblLocation: ContentPackageId
-- Guid, non-nullable, auto-assigned per existing row
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.tblLocation') AND name = N'ContentPackageId'
)
BEGIN
    ALTER TABLE dbo.tblLocation ADD ContentPackageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    PRINT 'Added tblLocation.ContentPackageId';
END
GO

-- tblScheduleEvent: ContentPackageId
-- Guid, non-nullable, auto-assigned per existing row
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.tblScheduleEvent') AND name = N'ContentPackageId'
)
BEGIN
    ALTER TABLE dbo.tblScheduleEvent ADD ContentPackageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    PRINT 'Added tblScheduleEvent.ContentPackageId';
END
GO

-- tblScheduleEvent: DefaultAssetId
-- int, nullable FK to tblAsset
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.tblScheduleEvent') AND name = N'DefaultAssetId'
)
BEGIN
    ALTER TABLE dbo.tblScheduleEvent ADD DefaultAssetId INT NULL;
    PRINT 'Added tblScheduleEvent.DefaultAssetId';
END
GO

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.tblScheduleEvent') AND name = N'DefaultAssetId'
)
AND NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.tblScheduleEvent')
      AND name = N'FK_tblScheduleEvent_DefaultAssetId'
)
BEGIN
    ALTER TABLE dbo.tblScheduleEvent
        ADD CONSTRAINT FK_tblScheduleEvent_DefaultAssetId
        FOREIGN KEY (DefaultAssetId) REFERENCES dbo.tblAsset (AssetId);
    PRINT 'Added FK_tblScheduleEvent_DefaultAssetId';
END
GO

-- tblDevice: Name
-- nvarchar(100), nullable
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.tblDevice') AND name = N'Name'
)
BEGIN
    ALTER TABLE dbo.tblDevice ADD Name NVARCHAR(100) NULL;
    PRINT 'Added tblDevice.Name';
END
GO

PRINT '============================================================';
PRINT 'Patch 4 complete.';
PRINT '============================================================';
