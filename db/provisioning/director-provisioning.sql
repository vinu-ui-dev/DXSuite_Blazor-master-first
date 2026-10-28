-- =============================================================================
-- DXSuite Director DB — Bootstrap Script (database only)
-- Database: DXSuite_Director
--
-- SCHEMA IS OWNED BY EF MIGRATIONS (DirectorDbContext). The full provisioning
-- flow for a fresh on-prem database is:
--   1. Run this script (creates the database with the right collation and
--      recovery model).
--   2. Apply the schema:
--        dotnet ef database update --project src/DXSuite.Director --context DirectorDbContext
--      (or run the delivered idempotent migration script via your DBA).
--   3. Run db/scripts/director-seed.sql to load the system lookup rows.
--
-- Do not add table DDL here; add an EF migration instead.
-- See db/schema-migration-strategy.md.
--
-- APPLICATION SERVICE ACCOUNT (DBA action, after the schema is applied):
--   Create the application login/user per site policy (SQL auth, Windows auth,
--   or AAD) and grant it db_datareader + db_datawriter on DXSuite_Director.
--   No other permissions are required; the application runs no DDL at runtime.
-- =============================================================================

-- 1. Create the database
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'DXSuite_Director')
BEGIN
    CREATE DATABASE DXSuite_Director COLLATE SQL_Latin1_General_CP1_CI_AS;
    PRINT 'Created database DXSuite_Director';
END
GO

-- 2. Simple recovery model: the transaction log truncates at each checkpoint,
-- preventing unbounded log growth on deployments that take full backups only
-- (no transaction log backup chain). Idempotent.
ALTER DATABASE DXSuite_Director SET RECOVERY SIMPLE;
PRINT 'Set recovery model to Simple';
GO

PRINT 'Director DB bootstrap complete. Apply schema via dotnet ef database update, then run director-seed.sql.';
GO
