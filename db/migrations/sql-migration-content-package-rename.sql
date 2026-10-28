-- =============================================================================
-- Content Package Rename Migration
-- =============================================================================
-- Renames all "Promotion"-named tables, columns, indexes, constraints, and the
-- seeded system user to their new "ContentPackage" names. Mirrors the code
-- rename that moved DXSuite.Director.Services.Promotion → Services.ContentPackage
-- and renamed the PromotionId cross-environment identifier to ContentPackageId.
--
-- This script is idempotent: each step is guarded so re-runs are safe. Intended
-- to be run exactly once against a database that has had the original
-- sql-migration-promotion-architecture.sql applied. Paired rollback:
-- sql-migration-content-package-rename-rollback.sql
--
-- Run against: DXSuite_Director database
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- -----------------------------------------------------------------------------
-- 1. Drop FK between history header and history item so tables can be renamed
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblPromotionHistoryItem_History')
    ALTER TABLE dbo.tblPromotionHistoryItem DROP CONSTRAINT FK_tblPromotionHistoryItem_History;

-- -----------------------------------------------------------------------------
-- 2. Rename the three promotion-specific tables
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblPromotionHistoryItem' AND schema_id = SCHEMA_ID('dbo'))
    EXEC sp_rename 'dbo.tblPromotionHistoryItem', 'tblContentPackageHistoryItem';

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblPromotionHistory' AND schema_id = SCHEMA_ID('dbo'))
    EXEC sp_rename 'dbo.tblPromotionHistory', 'tblContentPackageHistory';

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblPromotionEnvironmentLabel' AND schema_id = SCHEMA_ID('dbo'))
    EXEC sp_rename 'dbo.tblPromotionEnvironmentLabel', 'tblContentPackageEnvironmentLabel';

-- -----------------------------------------------------------------------------
-- 3. Rename columns inside the renamed tables
-- -----------------------------------------------------------------------------

-- tblContentPackageHistory.PromotionHistoryId → ContentPackageHistoryId
IF COL_LENGTH('dbo.tblContentPackageHistory', 'PromotionHistoryId') IS NOT NULL
    EXEC sp_rename 'dbo.tblContentPackageHistory.PromotionHistoryId', 'ContentPackageHistoryId', 'COLUMN';

-- tblContentPackageHistoryItem.*
IF COL_LENGTH('dbo.tblContentPackageHistoryItem', 'PromotionHistoryItemId') IS NOT NULL
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem.PromotionHistoryItemId', 'ContentPackageHistoryItemId', 'COLUMN';
IF COL_LENGTH('dbo.tblContentPackageHistoryItem', 'PromotionHistoryId') IS NOT NULL
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem.PromotionHistoryId', 'ContentPackageHistoryId', 'COLUMN';
IF COL_LENGTH('dbo.tblContentPackageHistoryItem', 'PromotionId') IS NOT NULL
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem.PromotionId', 'ContentPackageId', 'COLUMN';

-- tblContentPackageEnvironmentLabel.PromotionEnvironmentLabelId → ContentPackageEnvironmentLabelId
IF COL_LENGTH('dbo.tblContentPackageEnvironmentLabel', 'PromotionEnvironmentLabelId') IS NOT NULL
    EXEC sp_rename 'dbo.tblContentPackageEnvironmentLabel.PromotionEnvironmentLabelId', 'ContentPackageEnvironmentLabelId', 'COLUMN';

-- -----------------------------------------------------------------------------
-- 4. Rename primary keys, indexes, and default constraints on history tables
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_tblPromotionHistory')
    EXEC sp_rename 'dbo.PK_tblPromotionHistory', 'PK_tblContentPackageHistory';
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_tblPromotionHistoryItem')
    EXEC sp_rename 'dbo.PK_tblPromotionHistoryItem', 'PK_tblContentPackageHistoryItem';
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_tblPromotionEnvironmentLabel')
    EXEC sp_rename 'dbo.PK_tblPromotionEnvironmentLabel', 'PK_tblContentPackageEnvironmentLabel';

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblPromotionHistory_StartedAt' AND object_id = OBJECT_ID('dbo.tblContentPackageHistory'))
    EXEC sp_rename 'dbo.tblContentPackageHistory.IX_tblPromotionHistory_StartedAt', 'IX_tblContentPackageHistory_StartedAt', 'INDEX';
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblPromotionHistoryItem_HistoryId' AND object_id = OBJECT_ID('dbo.tblContentPackageHistoryItem'))
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem.IX_tblPromotionHistoryItem_HistoryId', 'IX_tblContentPackageHistoryItem_HistoryId', 'INDEX';
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblPromotionHistoryItem_PromotionId' AND object_id = OBJECT_ID('dbo.tblContentPackageHistoryItem'))
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem.IX_tblPromotionHistoryItem_PromotionId', 'IX_tblContentPackageHistoryItem_ContentPackageId', 'INDEX';

IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblPromotionHistory_TotalRecords')
    EXEC sp_rename 'dbo.DF_tblPromotionHistory_TotalRecords', 'DF_tblContentPackageHistory_TotalRecords';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblPromotionHistory_InsertedCount')
    EXEC sp_rename 'dbo.DF_tblPromotionHistory_InsertedCount', 'DF_tblContentPackageHistory_InsertedCount';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblPromotionHistory_UpdatedCount')
    EXEC sp_rename 'dbo.DF_tblPromotionHistory_UpdatedCount', 'DF_tblContentPackageHistory_UpdatedCount';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblPromotionHistory_SkippedCount')
    EXEC sp_rename 'dbo.DF_tblPromotionHistory_SkippedCount', 'DF_tblContentPackageHistory_SkippedCount';

IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_PromEnvLabel_SortOrder')
    EXEC sp_rename 'dbo.DF_PromEnvLabel_SortOrder', 'DF_CpkgEnvLabel_SortOrder';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_PromEnvLabel_CreateDate')
    EXEC sp_rename 'dbo.DF_PromEnvLabel_CreateDate', 'DF_CpkgEnvLabel_CreateDate';

-- -----------------------------------------------------------------------------
-- 5. Recreate the FK between history header and history item under new names
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblContentPackageHistoryItem_History')
BEGIN
    ALTER TABLE dbo.tblContentPackageHistoryItem
        ADD CONSTRAINT FK_tblContentPackageHistoryItem_History
            FOREIGN KEY (ContentPackageHistoryId)
            REFERENCES dbo.tblContentPackageHistory(ContentPackageHistoryId)
            ON DELETE CASCADE;
END

-- -----------------------------------------------------------------------------
-- 6. Rename PromotionId → ContentPackageId on the 13 in-scope entity tables.
-- -----------------------------------------------------------------------------
-- Same rename pattern for each: drop unique index, drop default constraint,
-- rename column, recreate default + unique index under new names.

DECLARE @tables TABLE (TableName sysname);
INSERT INTO @tables(TableName) VALUES
    ('tblLocation'),
    ('tblLocationGroup'),
    ('tblDisplay'),
    ('tblDisplayGroup'),
    ('tblAsset'),
    ('tblAssetGroup'),
    ('tblScheduleEvent'),
    ('tblConstraint'),
    ('tblAccessSchedule'),
    ('tblLocationAccessRule'),
    ('tblFloorPlanElement'),
    ('tblApprovalChain'),
    ('tblAssetContentMetadata');

DECLARE @t sysname;
DECLARE @sql nvarchar(max);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @tables;
OPEN cur;
FETCH NEXT FROM cur INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @fullTable nvarchar(300) = 'dbo.' + @t;
    DECLARE @oldIdx sysname = 'UX_' + @t + '_PromotionId';
    DECLARE @newIdx sysname = 'UX_' + @t + '_ContentPackageId';
    DECLARE @oldDef sysname = 'DF_' + @t + '_PromotionId';
    DECLARE @newDef sysname = 'DF_' + @t + '_ContentPackageId';

    -- Drop old unique index on PromotionId
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = @oldIdx AND object_id = OBJECT_ID(@fullTable))
    BEGIN
        SET @sql = N'DROP INDEX ' + QUOTENAME(@oldIdx) + N' ON ' + @fullTable;
        EXEC sp_executesql @sql;
    END

    -- Drop old default constraint on PromotionId
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = @oldDef)
    BEGIN
        SET @sql = N'ALTER TABLE ' + @fullTable + N' DROP CONSTRAINT ' + QUOTENAME(@oldDef);
        EXEC sp_executesql @sql;
    END

    -- Rename the column itself
    IF COL_LENGTH(@fullTable, 'PromotionId') IS NOT NULL
    BEGIN
        DECLARE @colQual nvarchar(400) = @fullTable + N'.PromotionId';
        EXEC sp_rename @objname = @colQual, @newname = 'ContentPackageId', @objtype = 'COLUMN';
    END

    -- Recreate default NEWID() under the new name
    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = @newDef)
    BEGIN
        SET @sql = N'ALTER TABLE ' + @fullTable +
                   N' ADD CONSTRAINT ' + QUOTENAME(@newDef) +
                   N' DEFAULT NEWID() FOR ContentPackageId';
        EXEC sp_executesql @sql;
    END

    -- Recreate unique nonclustered index under the new name
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = @newIdx AND object_id = OBJECT_ID(@fullTable))
    BEGIN
        SET @sql = N'CREATE UNIQUE NONCLUSTERED INDEX ' + QUOTENAME(@newIdx) +
                   N' ON ' + @fullTable + N'(ContentPackageId)';
        EXEC sp_executesql @sql;
    END

    FETCH NEXT FROM cur INTO @t;
END
CLOSE cur;
DEALLOCATE cur;

-- -----------------------------------------------------------------------------
-- 7. Rename the seeded system user
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.tblUser WHERE UserPrincipalName = 'promotion@dxsuite.system')
BEGIN
    UPDATE dbo.tblUser
    SET UserPrincipalName = 'contentpackage@dxsuite.system',
        FriendlyName      = 'Content Package System',
        GivenName         = 'Content Package',
        Surname           = 'System'
    WHERE UserPrincipalName = 'promotion@dxsuite.system';
END

COMMIT TRANSACTION;

PRINT 'Content Package rename migration applied successfully.';
