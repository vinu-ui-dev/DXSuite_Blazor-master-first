-- =============================================================================
-- One-time: align the DEV DXSuite_Design database with the re-baselined migrations.
--
-- Context: the Design migration history was tangled (12 stamped rows, 5 of which had
-- no repo file, and the phase K/M/N schema was applied by hand and never recorded as a
-- real migration). The repo has been re-baselined to a single InitialCreate generated
-- from the current model. The live schema was verified to already match that model, so
-- this script only fixes the bookkeeping. NO TABLE DDL RUNS and no data moves.
--
-- Run once against the dev Design DB only. Safe because the schema already matches the
-- InitialCreate model. After this, `dotnet ef database update` is a no-op against dev.
-- =============================================================================
USE DXSuite_Design;
GO

BEGIN TRANSACTION;

-- Replace the entire tangled history with the single clean baseline.
DELETE FROM [__EFMigrationsHistory];

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260622133128_InitialCreate', N'10.0.7');

COMMIT;
GO

PRINT 'Dev Design DB history reset to single InitialCreate baseline.';
GO
