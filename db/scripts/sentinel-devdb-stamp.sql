-- =============================================================================
-- One-time: bring the dev Sentinel database under EF migration tracking.
--
-- Context: the Sentinel DB was hand-provisioned with no __EFMigrationsHistory. The repo is now
-- baselined to a single InitialCreate generated from SentinelDBContext (25 tables). The live dev
-- DB already contains all 25 model tables (verified by table-set parity; SentinelTask sampled),
-- plus 4 unused legacy RIADirector-era tables the model ignores. This script only creates the
-- history table and records the baseline as applied. NO TABLE DDL RUNS.
--
-- Run once against the dev Sentinel DB (DXSuite_Sentinel / RIA_Sentinel) only.
--
-- Alternative: because Sentinel is runtime-disabled and the dev DB is a disposable port artifact,
-- it is equally valid to drop it and recreate clean from the migration
-- (dotnet ef database update against a fresh DB), which also sheds the 4 legacy tables.
-- =============================================================================

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId]    nvarchar(150) NOT NULL CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY,
        [ProductVersion] nvarchar(32)  NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260622135617_InitialCreate')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260622135617_InitialCreate', N'10.0.7');
GO

PRINT 'Sentinel dev DB stamped with InitialCreate baseline.';
GO
