/*
    Sentinel database upgrade to 2026.9.0.

    Takes a database at 20260625141809_AddDeviceSourceState to
    20260918153817_AddPulseIssueLevelDoNotReport, adding the three Pulse
    reporting tables. They start empty and nothing is backfilled. Idempotent:
    every step is guarded on __EFMigrationsHistory, so a database already at a
    later migration skips it. Requires SQL Server 2017 or later.

    Nothing here touches the customer's Pulse database, and Pulse reporting stays
    off until Sentinel:PulseReporting:Enabled is set. The Pulse login's GRANTs
    are applied by the customer's DBA, never by this script; see
    src/DXSuite.Host/docs/sentinel-pulse-reporting.md.

    Before running:
      1. Take the site offline.
      2. Back up the database.
      3. Stamp the database to its current migration if its history table is
         behind its schema. The guards read the history table only, so an
         unstamped database fails on objects that already exist.
      4. Run from the command line with -b so the first error stops the run, and
         -I because sqlcmd defaults QUOTED_IDENTIFIER off. Use -E for Windows
         authentication or -U <login> for SQL authentication:
             sqlcmd -S <server> -d <database> -E -b -I -i sentinel-upgrade-2026.9.0.sql

    The first batch makes no change and stops the run when AddDeviceSourceState
    is not in the migration history. In a query editor, run SET NOEXEC OFF in
    that window before fixing anything.
*/

-- A stopped run in an editor leaves NOEXEC on for the connection, which would
-- silently skip the fix and this rerun.
SET NOEXEC OFF;

-- A failed statement rolls back its whole migration rather than letting the
-- history row commit over a half-applied schema.
SET XACT_ABORT ON;
GO

-- Dynamic so a database with no history table reaches the message below
-- instead of failing to compile.
DECLARE @atBaseline bit = 0;
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NOT NULL
    EXEC sp_executesql
        N'SELECT @found = 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N''20260625141809_AddDeviceSourceState''',
        N'@found bit OUTPUT', @found = @atBaseline OUTPUT;

IF @atBaseline = 0
BEGIN
    PRINT N'Migration 20260625141809_AddDeviceSourceState is not applied. Check this is the Sentinel database and stamp it to its current migration.';
    RAISERROR('Upgrade stopped before any change. Resolve the problem printed above and run again. In a query editor, run SET NOEXEC OFF first or the fix will not execute.', 16, 1);
    -- Without -b the run would continue, so compile the remaining batches without executing them.
    SET NOEXEC ON;
END;
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918142916_AddPulseReporting'
)
BEGIN
    CREATE TABLE [SentinelPulseDeviceState] (
        [DeviceInstanceID] int NOT NULL,
        [PulseCustomerID] varchar(20) NOT NULL,
        [PulseDeviceID] varchar(50) NOT NULL,
        [LastHeartbeatSentUtc] datetime2 NULL,
        [LastAttemptUtc] datetime2 NULL,
        [LastSuccessUtc] datetime2 NULL,
        [ConsecutiveFailures] int NOT NULL,
        [SuppressionReason] varchar(30) NULL,
        [LastError] nvarchar(500) NULL,
        [LinkBrokenUtc] datetime2 NULL,
        CONSTRAINT [PK_SentinelPulseDeviceState] PRIMARY KEY ([DeviceInstanceID]),
        CONSTRAINT [FK_SentinelPulseDeviceState_SentinelDeviceInstance_DeviceInstanceID] FOREIGN KEY ([DeviceInstanceID]) REFERENCES [SentinelDeviceInstance] ([DeviceInstanceID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918142916_AddPulseReporting'
)
BEGIN
    CREATE TABLE [SentinelPulseIssueLevel] (
        [SentinelPulseIssueLevelID] int NOT NULL IDENTITY,
        [Kind] varchar(20) NOT NULL,
        [MonitorPointId] varchar(100) NULL,
        [Severity] varchar(20) NULL,
        [DirectorAlertKind] varchar(30) NULL,
        [ErrorLevel] tinyint NULL,
        [MonitorItemID] int NULL,
        [LastEditUser] varchar(100) NULL,
        [LastEditDate] datetime2 NOT NULL,
        CONSTRAINT [PK_SentinelPulseIssueLevel] PRIMARY KEY ([SentinelPulseIssueLevelID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918142916_AddPulseReporting'
)
BEGIN
    CREATE TABLE [SentinelPulseItemState] (
        [DeviceInstanceID] int NOT NULL,
        [MonitorItemID] int NOT NULL,
        [ErrorLevel] tinyint NOT NULL,
        [OpenIssueCodes] varchar(1000) NOT NULL,
        [Summary] varchar(500) NOT NULL,
        [LastPublishedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_SentinelPulseItemState] PRIMARY KEY ([DeviceInstanceID], [MonitorItemID]),
        CONSTRAINT [FK_SentinelPulseItemState_SentinelDeviceInstance_DeviceInstanceID] FOREIGN KEY ([DeviceInstanceID]) REFERENCES [SentinelDeviceInstance] ([DeviceInstanceID]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918142916_AddPulseReporting'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SentinelPulseIssueLevel_Kind_MonitorPointId_Severity_DirectorAlertKind] ON [SentinelPulseIssueLevel] ([Kind], [MonitorPointId], [Severity], [DirectorAlertKind]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918142916_AddPulseReporting'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918142916_AddPulseReporting', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918153817_AddPulseIssueLevelDoNotReport'
)
BEGIN
    ALTER TABLE [SentinelPulseIssueLevel] ADD [DoNotReport] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918153817_AddPulseIssueLevelDoNotReport'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918153817_AddPulseIssueLevelDoNotReport', N'10.0.7');
END;

COMMIT;
GO
