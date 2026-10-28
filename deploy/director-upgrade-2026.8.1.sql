BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818231004_AddPanels'
)
BEGIN
    CREATE TABLE [tblPanel] (
        [PanelId] int NOT NULL IDENTITY,
        [ContentPackageId] uniqueidentifier NOT NULL,
        [PanelKey] varchar(64) NOT NULL,
        [PanelName] nvarchar(100) NOT NULL,
        [LocationId] int NOT NULL,
        [AssetLibraryLocationId] int NULL,
        [BundleKey] varchar(64) NOT NULL,
        [PinnedVersion] varchar(32) NULL,
        [TokenHash] varchar(64) NOT NULL,
        [HotspotMapJson] nvarchar(max) NULL,
        [IsEnabled] bit NOT NULL DEFAULT CAST(1 AS bit),
        [LastSeenUtc] datetime NULL,
        [LastSeenIp] varchar(45) NULL,
        [CreateDate] datetime NOT NULL DEFAULT ((getdate())),
        [LastEditDate] datetime NULL,
        [LastEditUser] nvarchar(255) NULL,
        CONSTRAINT [PK_tblPanel] PRIMARY KEY ([PanelId]),
        CONSTRAINT [FK_tblPanel_tblLocation] FOREIGN KEY ([LocationId]) REFERENCES [tblLocation] ([LocationId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818231004_AddPanels'
)
BEGIN
    CREATE INDEX [IX_tblPanel_LocationId] ON [tblPanel] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818231004_AddPanels'
)
BEGIN
    CREATE UNIQUE INDEX [IX_tblPanel_PanelKey] ON [tblPanel] ([PanelKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818231004_AddPanels'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260818231004_AddPanels', N'10.0.7');
END;

COMMIT;
GO

