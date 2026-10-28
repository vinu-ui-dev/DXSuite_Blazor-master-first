-- =============================================================================
-- DXSuite Sentinel DB — Bootstrap Script (database + login only)
-- Database: DXSuite_Sentinel  (dev alias: RIA_Sentinel)
--
-- SCHEMA IS OWNED BY EF MIGRATIONS (SentinelDBContext). Apply it with:
--     dotnet ef database update --project src/DXSuite.Sentinel --context SentinelDBContext
-- Do not add table DDL here; add an EF migration instead. See db/schema-migration-strategy.md.
--
-- Legacy tables: older RIADirector-era Sentinel databases also carry four unused tables
-- (SentinelCloudPlatformConnector, SentinelLlmProviderConfig, SentinelLlmPromptConfig,
-- SentinelLlmInteraction). DXSuite does not use them; connector and LLM data come from the
-- Director database. The InitialCreate migration deliberately does not create them.
-- =============================================================================

-- 1. Create the database
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'DXSuite_Sentinel')
BEGIN
    CREATE DATABASE DXSuite_Sentinel COLLATE SQL_Latin1_General_CP1_CI_AS;
    PRINT 'Created database DXSuite_Sentinel';
END
GO

USE DXSuite_Sentinel;
GO

-- 2. Application login (least-privilege)
-- Replace <AppUserPassword> with a strong generated secret before running. The schema-level
-- GRANT (no DELETE) covers all current and migration-created tables, so this section is
-- order-independent and needs no per-table maintenance. No DELETE is intentional: deactivate
-- rows rather than hard-deleting.
IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = 'dxsuite_sentinel_app')
BEGIN
    CREATE LOGIN dxsuite_sentinel_app
        WITH PASSWORD = '<AppUserPassword>', CHECK_POLICY = ON, CHECK_EXPIRATION = ON;
    PRINT 'Created login dxsuite_sentinel_app';
END
GO

IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = 'dxsuite_sentinel_app')
BEGIN
    CREATE USER dxsuite_sentinel_app FOR LOGIN dxsuite_sentinel_app;
    GRANT SELECT, INSERT, UPDATE ON SCHEMA::dbo TO dxsuite_sentinel_app;
    PRINT 'Granted app user permissions (schema-level select/insert/update, no delete)';
END
GO

PRINT 'Sentinel DB bootstrap complete. Apply schema with: dotnet ef database update --context SentinelDBContext';
GO
