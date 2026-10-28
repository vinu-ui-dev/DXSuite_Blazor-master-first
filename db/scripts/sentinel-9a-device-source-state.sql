BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260625141809_AddDeviceSourceState'
)
BEGIN
    CREATE TABLE [SentinelDeviceCorrelationSuggestion] (
        [SuggestionID] int NOT NULL IDENTITY,
        [CustomerID] varchar(20) NOT NULL,
        [PrimaryDeviceInstanceID] int NOT NULL,
        [CandidateDeviceInstanceID] int NOT NULL,
        [Reason] varchar(200) NOT NULL,
        [Status] varchar(20) NOT NULL,
        [CreateDate] datetime NOT NULL,
        [LastEditDate] datetime NULL,
        CONSTRAINT [PK_SentinelDeviceCorrelationSuggestion] PRIMARY KEY ([SuggestionID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260625141809_AddDeviceSourceState'
)
BEGIN
    CREATE TABLE [SentinelDeviceSourceState] (
        [DeviceSourceStateID] int NOT NULL IDENTITY,
        [DeviceInstanceID] int NOT NULL,
        [SourceType] varchar(20) NOT NULL,
        [SourceKey] varchar(100) NOT NULL,
        [OverallHealth] int NOT NULL,
        [LastStateJson] varchar(max) NULL,
        [LastSeenUtc] datetime NOT NULL,
        [CustomerID] varchar(20) NOT NULL,
        [CreateDate] datetime NOT NULL,
        [LastEditDate] datetime NULL,
        CONSTRAINT [PK_SentinelDeviceSourceState] PRIMARY KEY ([DeviceSourceStateID]),
        CONSTRAINT [FK_SentinelDeviceSourceState_SentinelDeviceInstance_DeviceInstanceID] FOREIGN KEY ([DeviceInstanceID]) REFERENCES [SentinelDeviceInstance] ([DeviceInstanceID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260625141809_AddDeviceSourceState'
)
BEGIN
    CREATE INDEX [IX_SentinelDeviceCorrelationSuggestion_CustomerID_Status] ON [SentinelDeviceCorrelationSuggestion] ([CustomerID], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260625141809_AddDeviceSourceState'
)
BEGIN
    CREATE INDEX [IX_SentinelDeviceSourceState_CustomerID] ON [SentinelDeviceSourceState] ([CustomerID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260625141809_AddDeviceSourceState'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SentinelDeviceSourceState_DeviceInstanceID_SourceType_SourceKey] ON [SentinelDeviceSourceState] ([DeviceInstanceID], [SourceType], [SourceKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260625141809_AddDeviceSourceState'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260625141809_AddDeviceSourceState', N'10.0.7');
END;

COMMIT;
GO

