/*
    Director database upgrade to 2026.9.0.

    Takes a database at 20260818231004_AddPanels (2026.8.1 and 2026.8.2) to
    20260918201851_AddDeviceOrientation. Idempotent: every step is
    guarded on __EFMigrationsHistory, so a database already at a later migration
    skips it. Requires SQL Server 2017 or later.

    Before running:
      1. Take the site offline. This upgrade drops a column the 2026.8.x
         application still reads.
      2. Back up the database.
      3. Stamp the database to its current migration if its history table is
         behind its schema. The guards read the history table only, so an
         unstamped database fails on objects that already exist. A 2026.8.0
         database runs the 2026.8.1 script first.
      4. Run from the command line with -b so the first error stops the run, and
         -I because sqlcmd defaults QUOTED_IDENTIFIER off. Use -E for Windows
         authentication or -U <login> for SQL authentication:
             sqlcmd -S <server> -d <database> -E -b -I -i director-upgrade-2026.9.0.sql

    The first batch makes no change and stops the run when:
      - AddPanels is not in the migration history.
      - A Playlist location (LocationTypeId 7) has a display, before the
        metadata targeting migrations have run. Content targeting treats
        Playlist locations as having none.
      - tblMetadataDefinition holds a duplicate MetadataKey, before
        ReconcileMetadataDefinitionKeyIndex has run. That migration enforces
        uniqueness on the column.
      - A table the messaging migration creates already exists, before that
        migration has run. Rename the hand-made table (for example to
        tblMessage_Legacy) so the migration can create its own.
    Fix the reported rows and run the script again. In a query editor, run
    SET NOEXEC OFF in that window before fixing anything.

    After running, confirm tblMetadataDefinition has a unique index or
    constraint on MetadataKey (normally UQ_MetadataDefinition_Key).

    This release caches manifests in process for 30 minutes with no backplane.
    The environment must run DXSuite:Role = standalone on a single web node.
*/

-- A stopped run in an editor leaves NOEXEC on for the connection, which would
-- silently skip the fix and this rerun.
SET NOEXEC OFF;

-- A failed statement rolls back its whole migration rather than letting the
-- history row commit over a half-applied schema.
SET XACT_ABORT ON;
GO

DECLARE @problems nvarchar(max) = N'';

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818231004_AddPanels'
)
    SET @problems += N'Migration 20260818231004_AddPanels is not applied. Run the 2026.8.1 upgrade first.' + NCHAR(13) + NCHAR(10);

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152707_AddLocationMetadataRules'
)
BEGIN
    DECLARE @playlistDisplays nvarchar(max);
    SELECT @playlistDisplays = STRING_AGG(CAST(CONCAT('LocationId ', l.LocationId, ' (', l.LocationName, ')') AS nvarchar(max)), '; ')
    FROM tblLocation l
    WHERE l.LocationTypeId = 7
      AND EXISTS (SELECT 1 FROM tblDisplay d WHERE d.LocationId = l.LocationId);

    IF @playlistDisplays IS NOT NULL
        SET @problems += N'Playlist locations with displays: ' + @playlistDisplays + NCHAR(13) + NCHAR(10);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153317_ReconcileMetadataDefinitionKeyIndex'
)
BEGIN
    DECLARE @duplicateKeys nvarchar(max);
    SELECT @duplicateKeys = STRING_AGG(CAST(MetadataKey AS nvarchar(max)), '; ')
    FROM (SELECT MetadataKey FROM tblMetadataDefinition GROUP BY MetadataKey HAVING COUNT(*) > 1) k;

    IF @duplicateKeys IS NOT NULL
        SET @problems += N'Duplicate metadata keys: ' + @duplicateKeys + NCHAR(13) + NCHAR(10);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    DECLARE @messagingTables nvarchar(max);
    SELECT @messagingTables = STRING_AGG(CAST(t.name AS nvarchar(max)), '; ')
    FROM sys.tables t
    WHERE t.name IN (N'tblMessageType', N'tblZone', N'tblMessage', N'tblMessagePermission',
                     N'tblZoneDisplay', N'tblMessageActivation', N'tblMessageText', N'tblMessageActivationTarget');

    IF @messagingTables IS NOT NULL
        SET @problems += N'Tables the messaging migration creates already exist: ' + @messagingTables + NCHAR(13) + NCHAR(10);
END;

IF @problems <> N''
BEGIN
    PRINT @problems;
    RAISERROR('Upgrade stopped before any change. Resolve the problems printed above and run again. In a query editor, run SET NOEXEC OFF first or the fix will not execute.', 16, 1);
    -- Without -b the run would continue, so compile the remaining batches without executing them.
    SET NOEXEC ON;
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827035739_AddContentManifestLog'
)
BEGIN
    CREATE TABLE [tblContentManifestLog] (
        [ContentManifestLogId] int NOT NULL IDENTITY,
        [BucketType] varchar(10) NOT NULL,
        [BucketId] int NOT NULL,
        [FilterValue] nvarchar(255) NOT NULL,
        [Version] varchar(16) NOT NULL,
        [ItemsJson] nvarchar(max) NOT NULL,
        [ItemCount] int NOT NULL,
        [PlayCount] int NOT NULL,
        [EffectiveFromUtc] datetime NOT NULL,
        CONSTRAINT [PK_tblContentManifestLog] PRIMARY KEY ([ContentManifestLogId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827035739_AddContentManifestLog'
)
BEGIN
    CREATE INDEX [IX_tblContentManifestLog_Bucket] ON [tblContentManifestLog] ([BucketType], [BucketId], [FilterValue], [EffectiveFromUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827035739_AddContentManifestLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260827035739_AddContentManifestLog', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005327_AddDeviceTimeZone'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[tblDisplay]') AND [c].[name] = N'TimeZone');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [tblDisplay] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [tblDisplay] DROP COLUMN [TimeZone];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005327_AddDeviceTimeZone'
)
BEGIN
    ALTER TABLE [tblDevice] ADD [TimeZone] varchar(50) NOT NULL DEFAULT 'UNSET';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005327_AddDeviceTimeZone'
)
BEGIN
    ALTER TABLE [tblDevice] ADD [TimeZoneSource] varchar(20) NOT NULL DEFAULT 'Seeded';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005327_AddDeviceTimeZone'
)
BEGIN

    DECLARE @TimeZoneDefault sysname;
    SELECT @TimeZoneDefault = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c]
        ON [d].[parent_object_id] = [c].[object_id] AND [d].[parent_column_id] = [c].[column_id]
    WHERE [d].[parent_object_id] = OBJECT_ID(N'tblDevice') AND [c].[name] = N'TimeZone';
    IF @TimeZoneDefault IS NOT NULL
        EXEC(N'ALTER TABLE [tblDevice] DROP CONSTRAINT [' + @TimeZoneDefault + N']');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005327_AddDeviceTimeZone'
)
BEGIN

    DECLARE @TimeZoneSourceDefault sysname;
    SELECT @TimeZoneSourceDefault = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c]
        ON [d].[parent_object_id] = [c].[object_id] AND [d].[parent_column_id] = [c].[column_id]
    WHERE [d].[parent_object_id] = OBJECT_ID(N'tblDevice') AND [c].[name] = N'TimeZoneSource';
    IF @TimeZoneSourceDefault IS NOT NULL
        EXEC(N'ALTER TABLE [tblDevice] DROP CONSTRAINT [' + @TimeZoneSourceDefault + N']');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005327_AddDeviceTimeZone'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260913005327_AddDeviceTimeZone', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005449_AddSites'
)
BEGIN
    ALTER TABLE [tblDevice] ADD [SiteId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005449_AddSites'
)
BEGIN
    CREATE TABLE [tblSite] (
        [SiteId] int NOT NULL IDENTITY,
        [CustomerId] varchar(3) NOT NULL,
        [SiteName] nvarchar(100) NOT NULL,
        [Address1] nvarchar(255) NULL,
        [Address2] nvarchar(255) NULL,
        [City] nvarchar(100) NULL,
        [StateProvince] nvarchar(100) NULL,
        [PostalCode] varchar(20) NULL,
        [Country] nvarchar(100) NULL,
        CONSTRAINT [PK_tblSite] PRIMARY KEY ([SiteId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005449_AddSites'
)
BEGIN
    CREATE INDEX [IX_tblDevice_SiteId] ON [tblDevice] ([SiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005449_AddSites'
)
BEGIN
    ALTER TABLE [tblDevice] ADD CONSTRAINT [FK_tblDevice_tblSite] FOREIGN KEY ([SiteId]) REFERENCES [tblSite] ([SiteId]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913005449_AddSites'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260913005449_AddSites', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913030105_AddBrightSignArtifactKinds'
)
BEGIN
    ALTER TABLE [tblDevice] ADD [ClientBootloaderVersion] varchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913030105_AddBrightSignArtifactKinds'
)
BEGIN
    ALTER TABLE [tblDevice] ADD [TargetBootloaderVersionId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913030105_AddBrightSignArtifactKinds'
)
BEGIN
    ALTER TABLE [tblAppVersion] ADD [ArtifactKind] varchar(20) NOT NULL DEFAULT 'App';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913030105_AddBrightSignArtifactKinds'
)
BEGIN
    ALTER TABLE [tblAppVersion] ADD [MinBootloaderVersion] varchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913030105_AddBrightSignArtifactKinds'
)
BEGIN
    CREATE INDEX [IX_tblDevice_TargetBootloaderVersionId] ON [tblDevice] ([TargetBootloaderVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913030105_AddBrightSignArtifactKinds'
)
BEGIN
    ALTER TABLE [tblDevice] ADD CONSTRAINT [FK_tblDevice_tblAppVersion_TargetBootloaderVersionId] FOREIGN KEY ([TargetBootloaderVersionId]) REFERENCES [tblAppVersion] ([AppVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913030105_AddBrightSignArtifactKinds'
)
BEGIN

    IF NOT EXISTS (SELECT 1 FROM tblAppPlatform WHERE PlatformName = 'EngageBrightSign')
    BEGIN
        INSERT INTO tblAppPlatform (PlatformName, PlatformDescription, FolderName, IsActive, CreateDate)
        VALUES ('EngageBrightSign', 'Engage for BrightSign, native player flavour', 'EngageBrightSign', 1, GETUTCDATE());
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913030105_AddBrightSignArtifactKinds'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260913030105_AddBrightSignArtifactKinds', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    ALTER TABLE [tblDevicePairingRequest] ADD [DeliveryTokenCollectedAt] datetime NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    ALTER TABLE [tblDevicePairingRequest] ADD [DeliveryTokenCreatedAt] datetime NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    ALTER TABLE [tblDevicePairingRequest] ADD [DeliveryTokenProtected] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    ALTER TABLE [tblDevice] ADD [DwsPasswordProtected] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    ALTER TABLE [tblDevice] ADD [DwsPasswordRotatedAt] datetime NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    ALTER TABLE [tblDevice] ADD [ProvisioningFingerprint] varchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE TABLE [tblFirmwareImage] (
        [FirmwareImageId] int NOT NULL IDENTITY,
        [Family] varchar(10) NOT NULL,
        [VersionNumber] varchar(20) NOT NULL,
        [FileName] varchar(255) NOT NULL,
        [FilePath] varchar(500) NOT NULL,
        [FileSize] bigint NULL,
        [Sha256] varchar(64) NULL,
        [IsDefaultForFamily] bit NOT NULL,
        [UploadedBy] varchar(100) NULL,
        [UploadedDate] datetime NOT NULL,
        CONSTRAINT [PK_tblFirmwareImage] PRIMARY KEY ([FirmwareImageId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE TABLE [tblProvisioningPackage] (
        [ProvisioningPackageId] int NOT NULL IDENTITY,
        [PackageName] varchar(100) NOT NULL,
        [HardwareFamily] varchar(10) NOT NULL,
        [FirmwareImageId] int NOT NULL,
        [FirmwarePinOverridden] bit NOT NULL,
        [EndpointUrl] varchar(500) NOT NULL,
        [TimeSourceUrl] varchar(500) NULL,
        [CustomRootCaPem] nvarchar(max) NULL,
        [ProxyHostPort] varchar(100) NULL,
        [WifiSsid] varchar(100) NULL,
        [WifiPskProtected] nvarchar(max) NULL,
        [DwsPosition] varchar(20) NOT NULL,
        [BootstrapDwsPasswordProtected] nvarchar(max) NOT NULL,
        [WebInspectorEnabled] bit NOT NULL,
        [ConfigFingerprint] varchar(64) NOT NULL,
        [ZipFileName] varchar(255) NOT NULL,
        [ZipFilePath] varchar(500) NOT NULL,
        [ZipFileSize] bigint NULL,
        [ZipSha256] varchar(64) NULL,
        [IsRevoked] bit NOT NULL,
        [RevokedBy] varchar(100) NULL,
        [RevokedDate] datetime NULL,
        [CreatedBy] varchar(100) NULL,
        [CreatedDate] datetime NOT NULL,
        [ModifiedBy] varchar(100) NULL,
        [ModifiedDate] datetime NOT NULL,
        CONSTRAINT [PK_tblProvisioningPackage] PRIMARY KEY ([ProvisioningPackageId]),
        CONSTRAINT [FK_tblProvisioningPackage_tblFirmwareImage_FirmwareImageId] FOREIGN KEY ([FirmwareImageId]) REFERENCES [tblFirmwareImage] ([FirmwareImageId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE TABLE [tblProvisioningAuthorization] (
        [ProvisioningAuthorizationId] int NOT NULL IDENTITY,
        [SerialNumber] varchar(50) NOT NULL,
        [State] varchar(20) NOT NULL,
        [ArmedBy] varchar(100) NULL,
        [ArmedDate] datetime NOT NULL,
        [ExpiresDate] datetime NOT NULL,
        [PinnedAppVersionId] int NULL,
        [PinnedBootloaderVersionId] int NULL,
        [ProvisioningPackageId] int NULL,
        [ConvertedDate] datetime NULL,
        [ConvertedDeviceId] varchar(50) NULL,
        [DisarmedBy] varchar(100) NULL,
        [DisarmedDate] datetime NULL,
        CONSTRAINT [PK_tblProvisioningAuthorization] PRIMARY KEY ([ProvisioningAuthorizationId]),
        CONSTRAINT [FK_tblProvisioningAuthorization_tblAppVersion_PinnedAppVersionId] FOREIGN KEY ([PinnedAppVersionId]) REFERENCES [tblAppVersion] ([AppVersionId]),
        CONSTRAINT [FK_tblProvisioningAuthorization_tblAppVersion_PinnedBootloaderVersionId] FOREIGN KEY ([PinnedBootloaderVersionId]) REFERENCES [tblAppVersion] ([AppVersionId]),
        CONSTRAINT [FK_tblProvisioningAuthorization_tblProvisioningPackage_ProvisioningPackageId] FOREIGN KEY ([ProvisioningPackageId]) REFERENCES [tblProvisioningPackage] ([ProvisioningPackageId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE UNIQUE INDEX [UX_tblFirmwareImage_Family_Version] ON [tblFirmwareImage] ([Family], [VersionNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE INDEX [IX_tblProvisioningAuthorization_PinnedAppVersionId] ON [tblProvisioningAuthorization] ([PinnedAppVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE INDEX [IX_tblProvisioningAuthorization_PinnedBootloaderVersionId] ON [tblProvisioningAuthorization] ([PinnedBootloaderVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE INDEX [IX_tblProvisioningAuthorization_ProvisioningPackageId] ON [tblProvisioningAuthorization] ([ProvisioningPackageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE INDEX [IX_tblProvisioningAuthorization_Serial] ON [tblProvisioningAuthorization] ([SerialNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE INDEX [IX_tblProvisioningAuthorization_State] ON [tblProvisioningAuthorization] ([State]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE INDEX [IX_tblProvisioningPackage_Fingerprint] ON [tblProvisioningPackage] ([ConfigFingerprint]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    CREATE INDEX [IX_tblProvisioningPackage_FirmwareImageId] ON [tblProvisioningPackage] ([FirmwareImageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913031746_AddBrightSignProvisioning'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260913031746_AddBrightSignProvisioning', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913034813_AddPairingPlatform'
)
BEGIN
    ALTER TABLE [tblDevicePairingRequest] ADD [Platform] varchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913034813_AddPairingPlatform'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260913034813_AddPairingPlatform', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913185502_WidenAppVersionUniqueIndexToArtifactKind'
)
BEGIN

    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_tblAppVersion_Platform_Version' AND parent_object_id = OBJECT_ID('tblAppVersion'))
        ALTER TABLE tblAppVersion DROP CONSTRAINT UQ_tblAppVersion_Platform_Version;

    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_tblAppVersion_Platform_Kind_Version' AND parent_object_id = OBJECT_ID('tblAppVersion'))
        ALTER TABLE tblAppVersion ADD CONSTRAINT UQ_tblAppVersion_Platform_Kind_Version
            UNIQUE (PlatformId, ArtifactKind, VersionNumber);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913185502_WidenAppVersionUniqueIndexToArtifactKind'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260913185502_WidenAppVersionUniqueIndexToArtifactKind', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913210549_AddDeploymentAllowDowngrade'
)
BEGIN
    ALTER TABLE [tblUpdateDeployment] ADD [AllowDowngrade] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260913210549_AddDeploymentAllowDowngrade'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260913210549_AddDeploymentAllowDowngrade', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914000417_AddProvisioningBootstrapServedDate'
)
BEGIN
    ALTER TABLE [tblProvisioningAuthorization] ADD [BootstrapServedDate] datetime NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914000417_AddProvisioningBootstrapServedDate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260914000417_AddProvisioningBootstrapServedDate', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915204459_RemoveMoxiePlaylistLocationType'
)
BEGIN

    DELETE FROM tblLocationType
    WHERE ComponentTypeName = 'DXSuite.Director.Components.LocationTypes.PlaylistLocation_Omnivex'
      AND NOT EXISTS (SELECT 1 FROM tblLocation WHERE tblLocation.LocationTypeId = tblLocationType.LocationTypeId);

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915204459_RemoveMoxiePlaylistLocationType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260915204459_RemoveMoxiePlaylistLocationType', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152707_AddLocationMetadataRules'
)
BEGIN
    ALTER TABLE [tblLocation] ADD [MetadataMatchMode] varchar(10) NOT NULL DEFAULT 'all';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152707_AddLocationMetadataRules'
)
BEGIN
    ALTER TABLE [tblLocation] ADD [TargetContentByMetadata] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152707_AddLocationMetadataRules'
)
BEGIN
    CREATE TABLE [tblLocationMetadataRule] (
        [LocationMetadataRuleId] int NOT NULL IDENTITY,
        [ContentPackageId] uniqueidentifier NOT NULL DEFAULT (newid()),
        [LocationId] int NOT NULL,
        [MetadataKey] varchar(100) NOT NULL,
        [UntaggedContentPolicy] varchar(20) NOT NULL DEFAULT 'all',
        [UntaggedPlayerPolicy] varchar(20) NOT NULL DEFAULT 'all',
        [CreateDate] datetime NOT NULL DEFAULT ((getutcdate())),
        [LastEditDate] datetime NULL,
        [LastEditUser] varchar(50) NULL,
        CONSTRAINT [PK_tblLocationMetadataRule] PRIMARY KEY ([LocationMetadataRuleId]),
        CONSTRAINT [FK_LocationMetadataRule_Location] FOREIGN KEY ([LocationId]) REFERENCES [tblLocation] ([LocationId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152707_AddLocationMetadataRules'
)
BEGIN
    CREATE UNIQUE INDEX [UX_tblLocationMetadataRule_ContentPackageId] ON [tblLocationMetadataRule] ([ContentPackageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152707_AddLocationMetadataRules'
)
BEGIN
    CREATE UNIQUE INDEX [UX_tblLocationMetadataRule_LocationKey] ON [tblLocationMetadataRule] ([LocationId], [MetadataKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152707_AddLocationMetadataRules'
)
BEGIN
    EXEC('
    INSERT INTO tblLocationMetadataRule (LocationId, MetadataKey, LastEditUser)
    SELECT DISTINCT l.LocationId, LTRIM(RTRIM(COALESCE(JSON_VALUE(f.value, ''$.name''), JSON_VALUE(f.value, ''$.Name'')))), ''migration''
    FROM tblLocation l
    CROSS APPLY OPENJSON(l.ExtendedData) root
    CROSS APPLY OPENJSON(root.value) f
    WHERE ISJSON(l.ExtendedData) = 1
      AND root.[key] COLLATE Latin1_General_BIN IN (''customMetadataFields'', ''CustomMetadataFields'')
      AND root.type = 4
      AND LTRIM(RTRIM(COALESCE(JSON_VALUE(f.value, ''$.name''), JSON_VALUE(f.value, ''$.Name''), ''''))) <> '''';

    UPDATE tblLocation
    SET ExtendedData = JSON_MODIFY(JSON_MODIFY(ExtendedData, ''$.customMetadataFields'', NULL), ''$.CustomMetadataFields'', NULL)
    WHERE ISJSON(ExtendedData) = 1
      AND (JSON_QUERY(ExtendedData, ''$.customMetadataFields'') IS NOT NULL
        OR JSON_QUERY(ExtendedData, ''$.CustomMetadataFields'') IS NOT NULL);
    ')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152707_AddLocationMetadataRules'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260916152707_AddLocationMetadataRules', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152924_AllowMultiValueAssetMetadata'
)
BEGIN

    DECLARE @drop_tblAssetContentMetadata nvarchar(max) = N'';
    SELECT @drop_tblAssetContentMetadata = @drop_tblAssetContentMetadata +
        CASE WHEN i.is_unique_constraint = 1
            THEN N'ALTER TABLE tblAssetContentMetadata DROP CONSTRAINT ' + QUOTENAME(i.name) + N'; '
            ELSE N'DROP INDEX ' + QUOTENAME(i.name) + N' ON tblAssetContentMetadata; '
        END
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID('tblAssetContentMetadata')
      AND i.is_unique = 1
      AND i.is_primary_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0) = 2
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
             AND c.name IN ('AssetId', 'MetadataKey')) = 2;
    IF @drop_tblAssetContentMetadata <> N'' EXEC sp_executesql @drop_tblAssetContentMetadata;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152924_AllowMultiValueAssetMetadata'
)
BEGIN

    SELECT m.AssetContentMetadataId, m.AssetId, m.MetadataKey, m.MetadataValue, m.SetByUserId, m.CreateDate
    INTO #splitMetadata
    FROM tblAssetContentMetadata m
    JOIN tblMetadataDefinition d ON d.MetadataKey = m.MetadataKey
    WHERE d.FieldType = 'multi-select'
      AND ISJSON(d.OptionsJson) = 1
      AND m.MetadataValue LIKE '%,%'
      AND NOT EXISTS (
          SELECT 1 FROM STRING_SPLIT(m.MetadataValue, ',') piece
          WHERE NOT EXISTS (
              SELECT 1 FROM OPENJSON(d.OptionsJson) opt
              WHERE LTRIM(RTRIM(opt.value)) COLLATE DATABASE_DEFAULT = LTRIM(RTRIM(piece.value)) COLLATE DATABASE_DEFAULT));

    INSERT INTO tblAssetContentMetadata (AssetId, MetadataKey, MetadataValue, SetByUserId, CreateDate)
    SELECT DISTINCT s.AssetId, s.MetadataKey, LTRIM(RTRIM(piece.value)), s.SetByUserId, s.CreateDate
    FROM #splitMetadata s
    CROSS APPLY STRING_SPLIT(s.MetadataValue, ',') piece
    WHERE NOT EXISTS (
        SELECT 1 FROM tblAssetContentMetadata existing
        WHERE existing.AssetId = s.AssetId
          AND existing.MetadataKey = s.MetadataKey
          AND existing.MetadataValue = LTRIM(RTRIM(piece.value)));

    DELETE m
    FROM tblAssetContentMetadata m
    JOIN #splitMetadata s ON s.AssetContentMetadataId = m.AssetContentMetadataId;

    DECLARE @unsplitMetadata nvarchar(max);
    SELECT @unsplitMetadata = STRING_AGG(CAST(CONCAT('AssetId ', AssetId, ' key ', MetadataKey) AS nvarchar(max)), '; ')
    FROM tblAssetContentMetadata
    WHERE MetadataValue LIKE '%,%';
    IF @unsplitMetadata IS NOT NULL
        PRINT 'Asset metadata values containing a comma were left whole: ' + @unsplitMetadata;

    DROP TABLE #splitMetadata;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152924_AllowMultiValueAssetMetadata'
)
BEGIN

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('tblAssetContentMetadata') AND name = 'IX_ContentMetadata_AssetKey')
        CREATE INDEX IX_ContentMetadata_AssetKey ON tblAssetContentMetadata (AssetId, MetadataKey) INCLUDE (MetadataValue);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('tblAssetContentMetadata') AND name = 'UQ_ContentMetadata_AssetKeyValue')
        CREATE UNIQUE INDEX UQ_ContentMetadata_AssetKeyValue ON tblAssetContentMetadata (AssetId, MetadataKey, MetadataValue);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916152924_AllowMultiValueAssetMetadata'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260916152924_AllowMultiValueAssetMetadata', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153123_AddAssetOwnership'
)
BEGIN
    ALTER TABLE [tblLocationAccessRule] ADD [ViewScope] varchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153123_AddAssetOwnership'
)
BEGIN
    EXEC('UPDATE tblLocationAccessRule SET ViewScope = CASE WHEN MetadataFilterKey = ''__all__'' THEN ''all'' ELSE ''none'' END WHERE AccessLevel = ''view''')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153123_AddAssetOwnership'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[tblLocationAccessRule]') AND [c].[name] = N'MetadataFilterKey');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [tblLocationAccessRule] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [tblLocationAccessRule] DROP COLUMN [MetadataFilterKey];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153123_AddAssetOwnership'
)
BEGIN
    ALTER TABLE [tblAsset] ADD [UploadedByUserGroupId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153123_AddAssetOwnership'
)
BEGIN
    ALTER TABLE [tblAsset] ADD [UploadedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153123_AddAssetOwnership'
)
BEGIN
    CREATE INDEX [IX_tblAsset_UploadedByUserGroupId] ON [tblAsset] ([UploadedByUserGroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153123_AddAssetOwnership'
)
BEGIN
    CREATE INDEX [IX_tblAsset_UploadedByUserId] ON [tblAsset] ([UploadedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153123_AddAssetOwnership'
)
BEGIN
    ALTER TABLE [tblAsset] ADD CONSTRAINT [FK_Asset_UploadedByUser] FOREIGN KEY ([UploadedByUserId]) REFERENCES [tblUser] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153123_AddAssetOwnership'
)
BEGIN
    ALTER TABLE [tblAsset] ADD CONSTRAINT [FK_Asset_UploadedByUserGroup] FOREIGN KEY ([UploadedByUserGroupId]) REFERENCES [tblUserGroup] ([UserGroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153123_AddAssetOwnership'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260916153123_AddAssetOwnership', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153217_AllowMultiValueUserMetadata'
)
BEGIN

    DECLARE @drop_tblUserMetadata nvarchar(max) = N'';
    SELECT @drop_tblUserMetadata = @drop_tblUserMetadata +
        CASE WHEN i.is_unique_constraint = 1
            THEN N'ALTER TABLE tblUserMetadata DROP CONSTRAINT ' + QUOTENAME(i.name) + N'; '
            ELSE N'DROP INDEX ' + QUOTENAME(i.name) + N' ON tblUserMetadata; '
        END
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID('tblUserMetadata')
      AND i.is_unique = 1
      AND i.is_primary_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0) = 2
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
             AND c.name IN ('UserId', 'MetadataKey')) = 2;
    IF @drop_tblUserMetadata <> N'' EXEC sp_executesql @drop_tblUserMetadata;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153217_AllowMultiValueUserMetadata'
)
BEGIN

    DECLARE @drop_tblUserGroupMetadata nvarchar(max) = N'';
    SELECT @drop_tblUserGroupMetadata = @drop_tblUserGroupMetadata +
        CASE WHEN i.is_unique_constraint = 1
            THEN N'ALTER TABLE tblUserGroupMetadata DROP CONSTRAINT ' + QUOTENAME(i.name) + N'; '
            ELSE N'DROP INDEX ' + QUOTENAME(i.name) + N' ON tblUserGroupMetadata; '
        END
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID('tblUserGroupMetadata')
      AND i.is_unique = 1
      AND i.is_primary_key = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0) = 2
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
             AND c.name IN ('UserGroupId', 'MetadataKey')) = 2;
    IF @drop_tblUserGroupMetadata <> N'' EXEC sp_executesql @drop_tblUserGroupMetadata;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153217_AllowMultiValueUserMetadata'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260916153217_AllowMultiValueUserMetadata', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153317_ReconcileMetadataDefinitionKeyIndex'
)
BEGIN

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID('tblMetadataDefinition')
          AND i.is_unique = 1
          AND i.is_primary_key = 0
          AND (SELECT COUNT(*) FROM sys.index_columns ic
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0) = 1
          AND EXISTS (SELECT 1 FROM sys.index_columns ic
                      JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND c.name = 'MetadataKey'))
        ALTER TABLE tblMetadataDefinition ADD CONSTRAINT UQ_MetadataDefinition_Key UNIQUE (MetadataKey);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916153317_ReconcileMetadataDefinitionKeyIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260916153317_ReconcileMetadataDefinitionKeyIndex', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE TABLE [tblMessageType] (
        [MessageTypeId] int NOT NULL IDENTITY,
        [Code] varchar(30) NOT NULL,
        [DisplayName] nvarchar(100) NOT NULL,
        [Priority] int NOT NULL,
        [DisplayMode] tinyint NOT NULL,
        [AllowsSignVideo] bit NOT NULL,
        [DefaultDisplaySeconds] int NOT NULL,
        [DefaultHideSeconds] int NOT NULL,
        CONSTRAINT [PK_tblMessageType] PRIMARY KEY ([MessageTypeId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE TABLE [tblZone] (
        [ZoneId] int NOT NULL IDENTITY,
        [ZoneName] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [SortOrder] int NOT NULL,
        [CreatedUtc] datetime2 NOT NULL DEFAULT ((sysutcdatetime())),
        CONSTRAINT [PK_tblZone] PRIMARY KEY ([ZoneId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE TABLE [tblMessage] (
        [MessageId] int NOT NULL IDENTITY,
        [MessageTypeId] int NOT NULL,
        [OwningUserGroupId] int NULL,
        [Name] nvarchar(100) NOT NULL,
        [IsCanned] bit NOT NULL,
        [SignVideoAssetId] int NULL,
        [Status] tinyint NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedUtc] datetime2 NOT NULL DEFAULT ((sysutcdatetime())),
        [CreatedByUserId] int NULL,
        [ModifiedUtc] datetime2 NULL,
        [ModifiedByUserId] int NULL,
        CONSTRAINT [PK_tblMessage] PRIMARY KEY ([MessageId]),
        CONSTRAINT [FK_tblMessage_OwningUserGroup] FOREIGN KEY ([OwningUserGroupId]) REFERENCES [tblUserGroup] ([UserGroupId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_tblMessage_SignVideoAsset] FOREIGN KEY ([SignVideoAssetId]) REFERENCES [tblAsset] ([AssetId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_tblMessage_tblMessageType] FOREIGN KEY ([MessageTypeId]) REFERENCES [tblMessageType] ([MessageTypeId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE TABLE [tblMessagePermission] (
        [MessagePermissionId] int NOT NULL IDENTITY,
        [UserGroupId] int NOT NULL,
        [MessageTypeId] int NOT NULL,
        [ZoneId] int NULL,
        [CanFreeText] bit NOT NULL,
        CONSTRAINT [PK_tblMessagePermission] PRIMARY KEY ([MessagePermissionId]),
        CONSTRAINT [FK_tblMessagePermission_UserGroup] FOREIGN KEY ([UserGroupId]) REFERENCES [tblUserGroup] ([UserGroupId]) ON DELETE CASCADE,
        CONSTRAINT [FK_tblMessagePermission_tblMessageType] FOREIGN KEY ([MessageTypeId]) REFERENCES [tblMessageType] ([MessageTypeId]) ON DELETE CASCADE,
        CONSTRAINT [FK_tblMessagePermission_tblZone] FOREIGN KEY ([ZoneId]) REFERENCES [tblZone] ([ZoneId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE TABLE [tblZoneDisplay] (
        [ZoneId] int NOT NULL,
        [DisplayId] int NOT NULL,
        CONSTRAINT [PK_tblZoneDisplay] PRIMARY KEY ([ZoneId], [DisplayId]),
        CONSTRAINT [FK_tblZoneDisplay_tblDisplay] FOREIGN KEY ([DisplayId]) REFERENCES [tblDisplay] ([DisplayId]) ON DELETE CASCADE,
        CONSTRAINT [FK_tblZoneDisplay_tblZone] FOREIGN KEY ([ZoneId]) REFERENCES [tblZone] ([ZoneId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE TABLE [tblMessageActivation] (
        [MessageActivationId] int NOT NULL IDENTITY,
        [MessageId] int NOT NULL,
        [StartUtc] datetime2 NOT NULL,
        [EndUtc] datetime2 NULL,
        [PriorityOverride] int NULL,
        [DisplaySecondsOverride] int NULL,
        [HideSecondsOverride] int NULL,
        [Source] tinyint NOT NULL,
        [ActivatedByUserId] int NULL,
        [ActivatedUtc] datetime2 NOT NULL DEFAULT ((sysutcdatetime())),
        [CancelledByUserId] int NULL,
        [CancelledUtc] datetime2 NULL,
        [ContentSnapshot] nvarchar(max) NULL,
        CONSTRAINT [PK_tblMessageActivation] PRIMARY KEY ([MessageActivationId]),
        CONSTRAINT [FK_tblMessageActivation_tblMessage] FOREIGN KEY ([MessageId]) REFERENCES [tblMessage] ([MessageId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE TABLE [tblMessageText] (
        [MessageId] int NOT NULL,
        [LanguageCode] varchar(10) NOT NULL,
        [Body] nvarchar(500) NOT NULL,
        CONSTRAINT [PK_tblMessageText] PRIMARY KEY ([MessageId], [LanguageCode]),
        CONSTRAINT [FK_tblMessageText_tblMessage] FOREIGN KEY ([MessageId]) REFERENCES [tblMessage] ([MessageId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE TABLE [tblMessageActivationTarget] (
        [MessageActivationTargetId] int NOT NULL IDENTITY,
        [MessageActivationId] int NOT NULL,
        [ZoneId] int NULL,
        [DisplayId] int NULL,
        [LocationId] int NULL,
        CONSTRAINT [PK_tblMessageActivationTarget] PRIMARY KEY ([MessageActivationTargetId]),
        CONSTRAINT [CK_tblMessageActivationTarget_OneTarget] CHECK ((CASE WHEN [ZoneId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [DisplayId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [LocationId] IS NULL THEN 0 ELSE 1 END) = 1),
        CONSTRAINT [FK_tblMessageActivationTarget_tblDisplay] FOREIGN KEY ([DisplayId]) REFERENCES [tblDisplay] ([DisplayId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_tblMessageActivationTarget_tblLocation] FOREIGN KEY ([LocationId]) REFERENCES [tblLocation] ([LocationId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_tblMessageActivationTarget_tblMessageActivation] FOREIGN KEY ([MessageActivationId]) REFERENCES [tblMessageActivation] ([MessageActivationId]) ON DELETE CASCADE,
        CONSTRAINT [FK_tblMessageActivationTarget_tblZone] FOREIGN KEY ([ZoneId]) REFERENCES [tblZone] ([ZoneId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'MessageTypeId', N'AllowsSignVideo', N'Code', N'DefaultDisplaySeconds', N'DefaultHideSeconds', N'DisplayMode', N'DisplayName', N'Priority') AND [object_id] = OBJECT_ID(N'[tblMessageType]'))
        SET IDENTITY_INSERT [tblMessageType] ON;
    EXEC(N'INSERT INTO [tblMessageType] ([MessageTypeId], [AllowsSignVideo], [Code], [DefaultDisplaySeconds], [DefaultHideSeconds], [DisplayMode], [DisplayName], [Priority])
    VALUES (1, CAST(1 AS bit), ''EMERGENCY'', 10, 30, CAST(1 AS tinyint), N''Emergency'', 10),
    (2, CAST(0 AS bit), ''IRROPS'', 10, 30, CAST(2 AS tinyint), N''IRROPS'', 20),
    (3, CAST(1 AS bit), ''VISUAL_PAGING'', 10, 30, CAST(2 AS tinyint), N''Visual Paging'', 40),
    (4, CAST(1 AS bit), ''COUNTER'', 10, 30, CAST(3 AS tinyint), N''Counter'', 50)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'MessageTypeId', N'AllowsSignVideo', N'Code', N'DefaultDisplaySeconds', N'DefaultHideSeconds', N'DisplayMode', N'DisplayName', N'Priority') AND [object_id] = OBJECT_ID(N'[tblMessageType]'))
        SET IDENTITY_INSERT [tblMessageType] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessage_MessageTypeId] ON [tblMessage] ([MessageTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessage_OwningUserGroupId] ON [tblMessage] ([OwningUserGroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessage_SignVideoAssetId] ON [tblMessage] ([SignVideoAssetId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessageActivation_MessageId] ON [tblMessageActivation] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessageActivation_Window] ON [tblMessageActivation] ([StartUtc], [EndUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessageActivationTarget_DisplayId] ON [tblMessageActivationTarget] ([DisplayId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessageActivationTarget_LocationId] ON [tblMessageActivationTarget] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessageActivationTarget_MessageActivationId] ON [tblMessageActivationTarget] ([MessageActivationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessageActivationTarget_ZoneId] ON [tblMessageActivationTarget] ([ZoneId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessagePermission_MessageTypeId] ON [tblMessagePermission] ([MessageTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblMessagePermission_ZoneId] ON [tblMessagePermission] ([ZoneId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [UX_tblMessagePermission_Group_Type_Zone] ON [tblMessagePermission] ([UserGroupId], [MessageTypeId], [ZoneId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [UX_tblMessageType_Code] ON [tblMessageType] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [UX_tblZone_ZoneName] ON [tblZone] ([ZoneName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    CREATE INDEX [IX_tblZoneDisplay_DisplayId] ON [tblZoneDisplay] ([DisplayId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918125913_AddMessaging'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918125913_AddMessaging', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918142924_AddDirectorUrlReportingDevice'
)
BEGIN
    ALTER TABLE [tblDirectorUrl] ADD [DeviceInstanceId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918142924_AddDirectorUrlReportingDevice'
)
BEGIN
    CREATE INDEX [IX_tblDirectorUrl_DeviceInstanceId] ON [tblDirectorUrl] ([DeviceInstanceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918142924_AddDirectorUrlReportingDevice'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918142924_AddDirectorUrlReportingDevice', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918201851_AddDeviceOrientation'
)
BEGIN
    ALTER TABLE [tblDevice] ADD [Orientation] varchar(20) NOT NULL DEFAULT 'Landscape';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918201851_AddDeviceOrientation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918201851_AddDeviceOrientation', N'10.0.7');
END;

COMMIT;
GO
