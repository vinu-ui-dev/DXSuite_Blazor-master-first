-- =============================================================================
-- One-time: bring a Director database under EF migration tracking.
--
-- Context: Director was hand-provisioned with no __EFMigrationsHistory. The repo now has a single
-- InitialCreate baseline generated from DirectorDbContext (76 tables). The current DXSuite Director
-- schema matches that baseline table-for-table. This script only creates the history table and
-- records the baseline as applied. NO TABLE DDL RUNS and no data moves.
--
-- RUN PER ENVIRONMENT, AND VERIFY FIRST. Director is the production database for the on-prem
-- deployments, and different environments may sit at slightly different patch levels. Before running
-- this against any environment, confirm that environment's schema matches the InitialCreate model
-- (table and key-column level). If an environment is behind, bring it current with the delta first,
-- then stamp. Do not stamp a database whose schema does not match the baseline.
-- =============================================================================
USE DXSuite_Director;   -- adjust to the environment's Director database name
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId]    nvarchar(150) NOT NULL CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY,
        [ProductVersion] nvarchar(32)  NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260622144321_InitialCreate')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260622144321_InitialCreate', N'10.0.7');
GO

PRINT 'Director DB stamped with InitialCreate baseline.';
GO
