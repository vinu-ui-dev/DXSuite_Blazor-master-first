-- =============================================================================
-- Content Package Rename Migration — ROLLBACK
-- =============================================================================
-- Reverses sql-migration-content-package-rename.sql, restoring the original
-- "Promotion"-prefixed names on tables, columns, indexes, constraints, and the
-- seeded system user. Paired with the forward rename script.
--
-- Idempotent: guarded with existence checks so re-runs are safe.
--
-- Run against: DXSuite_Director database
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- -----------------------------------------------------------------------------
-- 1. Drop the renamed FK so tables can be renamed back
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblContentPackageHistoryItem_History')
    ALTER TABLE dbo.tblContentPackageHistoryItem DROP CONSTRAINT FK_tblContentPackageHistoryItem_History;

-- -----------------------------------------------------------------------------
-- 2. Revert ContentPackageId → PromotionId on the 13 entity tables
-- -----------------------------------------------------------------------------
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
    DECLARE @newIdx sysname = 'UX_' + @t + '_ContentPackageId';
    DECLARE @oldIdx sysname = 'UX_' + @t + '_PromotionId';
    DECLARE @newDef sysname = 'DF_' + @t + '_ContentPackageId';
    DECLARE @oldDef sysname = 'DF_' + @t + '_PromotionId';

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = @newIdx AND object_id = OBJECT_ID(@fullTable))
    BEGIN
        SET @sql = N'DROP INDEX ' + QUOTENAME(@newIdx) + N' ON ' + @fullTable;
        EXEC sp_executesql @sql;
    END

    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = @newDef)
    BEGIN
        SET @sql = N'ALTER TABLE ' + @fullTable + N' DROP CONSTRAINT ' + QUOTENAME(@newDef);
        EXEC sp_executesql @sql;
    END

    IF COL_LENGTH(@fullTable, 'ContentPackageId') IS NOT NULL
    BEGIN
        DECLARE @colQual nvarchar(400) = @fullTable + N'.ContentPackageId';
        EXEC sp_rename @objname = @colQual, @newname = 'PromotionId', @objtype = 'COLUMN';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = @oldDef)
    BEGIN
        SET @sql = N'ALTER TABLE ' + @fullTable +
                   N' ADD CONSTRAINT ' + QUOTENAME(@oldDef) +
                   N' DEFAULT NEWID() FOR PromotionId';
        EXEC sp_executesql @sql;
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = @oldIdx AND object_id = OBJECT_ID(@fullTable))
    BEGIN
        SET @sql = N'CREATE UNIQUE NONCLUSTERED INDEX ' + QUOTENAME(@oldIdx) +
                   N' ON ' + @fullTable + N'(PromotionId)';
        EXEC sp_executesql @sql;
    END

    FETCH NEXT FROM cur INTO @t;
END
CLOSE cur;
DEALLOCATE cur;

-- -----------------------------------------------------------------------------
-- 3. Revert default constraints and indexes on history tables
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_CpkgEnvLabel_SortOrder')
    EXEC sp_rename 'dbo.DF_CpkgEnvLabel_SortOrder', 'DF_PromEnvLabel_SortOrder';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_CpkgEnvLabel_CreateDate')
    EXEC sp_rename 'dbo.DF_CpkgEnvLabel_CreateDate', 'DF_PromEnvLabel_CreateDate';

IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblContentPackageHistory_TotalRecords')
    EXEC sp_rename 'dbo.DF_tblContentPackageHistory_TotalRecords', 'DF_tblPromotionHistory_TotalRecords';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblContentPackageHistory_InsertedCount')
    EXEC sp_rename 'dbo.DF_tblContentPackageHistory_InsertedCount', 'DF_tblPromotionHistory_InsertedCount';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblContentPackageHistory_UpdatedCount')
    EXEC sp_rename 'dbo.DF_tblContentPackageHistory_UpdatedCount', 'DF_tblPromotionHistory_UpdatedCount';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_tblContentPackageHistory_SkippedCount')
    EXEC sp_rename 'dbo.DF_tblContentPackageHistory_SkippedCount', 'DF_tblPromotionHistory_SkippedCount';

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblContentPackageHistory_StartedAt' AND object_id = OBJECT_ID('dbo.tblContentPackageHistory'))
    EXEC sp_rename 'dbo.tblContentPackageHistory.IX_tblContentPackageHistory_StartedAt', 'IX_tblPromotionHistory_StartedAt', 'INDEX';
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblContentPackageHistoryItem_HistoryId' AND object_id = OBJECT_ID('dbo.tblContentPackageHistoryItem'))
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem.IX_tblContentPackageHistoryItem_HistoryId', 'IX_tblPromotionHistoryItem_HistoryId', 'INDEX';
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblContentPackageHistoryItem_ContentPackageId' AND object_id = OBJECT_ID('dbo.tblContentPackageHistoryItem'))
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem.IX_tblContentPackageHistoryItem_ContentPackageId', 'IX_tblPromotionHistoryItem_PromotionId', 'INDEX';

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_tblContentPackageHistory')
    EXEC sp_rename 'dbo.PK_tblContentPackageHistory', 'PK_tblPromotionHistory';
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_tblContentPackageHistoryItem')
    EXEC sp_rename 'dbo.PK_tblContentPackageHistoryItem', 'PK_tblPromotionHistoryItem';
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_tblContentPackageEnvironmentLabel')
    EXEC sp_rename 'dbo.PK_tblContentPackageEnvironmentLabel', 'PK_tblPromotionEnvironmentLabel';

-- -----------------------------------------------------------------------------
-- 4. Revert column renames inside history tables
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.tblContentPackageEnvironmentLabel', 'ContentPackageEnvironmentLabelId') IS NOT NULL
    EXEC sp_rename 'dbo.tblContentPackageEnvironmentLabel.ContentPackageEnvironmentLabelId', 'PromotionEnvironmentLabelId', 'COLUMN';

IF COL_LENGTH('dbo.tblContentPackageHistoryItem', 'ContentPackageId') IS NOT NULL
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem.ContentPackageId', 'PromotionId', 'COLUMN';
IF COL_LENGTH('dbo.tblContentPackageHistoryItem', 'ContentPackageHistoryId') IS NOT NULL
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem.ContentPackageHistoryId', 'PromotionHistoryId', 'COLUMN';
IF COL_LENGTH('dbo.tblContentPackageHistoryItem', 'ContentPackageHistoryItemId') IS NOT NULL
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem.ContentPackageHistoryItemId', 'PromotionHistoryItemId', 'COLUMN';

IF COL_LENGTH('dbo.tblContentPackageHistory', 'ContentPackageHistoryId') IS NOT NULL
    EXEC sp_rename 'dbo.tblContentPackageHistory.ContentPackageHistoryId', 'PromotionHistoryId', 'COLUMN';

-- -----------------------------------------------------------------------------
-- 5. Revert table renames
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblContentPackageEnvironmentLabel' AND schema_id = SCHEMA_ID('dbo'))
    EXEC sp_rename 'dbo.tblContentPackageEnvironmentLabel', 'tblPromotionEnvironmentLabel';

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblContentPackageHistoryItem' AND schema_id = SCHEMA_ID('dbo'))
    EXEC sp_rename 'dbo.tblContentPackageHistoryItem', 'tblPromotionHistoryItem';

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'tblContentPackageHistory' AND schema_id = SCHEMA_ID('dbo'))
    EXEC sp_rename 'dbo.tblContentPackageHistory', 'tblPromotionHistory';

-- -----------------------------------------------------------------------------
-- 6. Recreate the FK under the original name
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tblPromotionHistoryItem_History')
BEGIN
    ALTER TABLE dbo.tblPromotionHistoryItem
        ADD CONSTRAINT FK_tblPromotionHistoryItem_History
            FOREIGN KEY (PromotionHistoryId)
            REFERENCES dbo.tblPromotionHistory(PromotionHistoryId)
            ON DELETE CASCADE;
END

-- -----------------------------------------------------------------------------
-- 7. Revert the seeded system user
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.tblUser WHERE UserPrincipalName = 'contentpackage@dxsuite.system')
BEGIN
    UPDATE dbo.tblUser
    SET UserPrincipalName = 'promotion@dxsuite.system',
        FriendlyName      = 'Promotion System',
        GivenName         = 'Promotion',
        Surname           = 'System'
    WHERE UserPrincipalName = 'contentpackage@dxsuite.system';
END

COMMIT TRANSACTION;

PRINT 'Content Package rename rollback applied successfully.';
