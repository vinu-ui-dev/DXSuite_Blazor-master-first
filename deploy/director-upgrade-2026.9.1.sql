/*
    Director database upgrade to 2026.9.1.

    Takes a database at 20260918201851_AddDeviceOrientation (2026.9.0) to
    20260923200058_AddDeviceVideoPath. Idempotent: every step is guarded on
    __EFMigrationsHistory, so a database already at a later migration skips it.

    The only change adds tblDevice.VideoPath with a default, which the 2026.9.0
    application ignores. The database can therefore be upgraded while the site
    is still online, and before the 2026.9.1 binaries are deployed.

    Before running:
      1. Back up the database.
      2. A database below 2026.9.0 runs the 2026.9.0 upgrade first.
      3. Run from the command line with -b so the first error stops the run, and
         -I because sqlcmd defaults QUOTED_IDENTIFIER off. Use -E for Windows
         authentication or -U <login> for SQL authentication:
             sqlcmd -S <server> -d <database> -E -b -I -i director-upgrade-2026.9.1.sql

    Every device starts on the hardware video plane. Seamless transitions are
    turned on per device afterwards, in Device Settings.
*/

-- A stopped run in an editor leaves NOEXEC on for the connection, which would
-- silently skip the fix and this rerun.
SET NOEXEC OFF;

SET XACT_ABORT ON;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918201851_AddDeviceOrientation'
)
BEGIN
    RAISERROR('Upgrade stopped before any change. Migration 20260918201851_AddDeviceOrientation is not applied; run the 2026.9.0 upgrade first. In a query editor, run SET NOEXEC OFF before running again.', 16, 1);
    -- Without -b the run would continue, so compile the remaining batches without executing them.
    SET NOEXEC ON;
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923200058_AddDeviceVideoPath'
)
BEGIN
    ALTER TABLE [tblDevice] ADD [VideoPath] varchar(10) NOT NULL DEFAULT 'Hardware';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923200058_AddDeviceVideoPath'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923200058_AddDeviceVideoPath', N'10.0.7');
END;

COMMIT;
GO

