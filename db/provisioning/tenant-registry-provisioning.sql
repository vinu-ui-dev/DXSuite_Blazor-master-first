-- =============================================================================
-- DXSuite Tenant Registry DB — Bootstrap Script (database + login only)
-- Database: DXSuite_TenantRegistry
--
-- SCHEMA IS OWNED BY EF MIGRATIONS (TenantRegistryDbContext). Apply it with:
--     dotnet ef database update --project src/DXSuite.Shared --context TenantRegistryDbContext
-- Do not add table DDL here; add an EF migration instead. See db/schema-migration-strategy.md.
--
-- Tenant onboarding runbook: the previous version of this file also held the "add a tenant" and
-- "seed the tenant Director database" operational templates. Those are preserved in the archived
-- original (dxsuite-archive/original-provisioning) and will be reworked with the Director migrator
-- (the Director seed is itself hand-SQL DDL the migrator will own). They are not duplicated here.
-- =============================================================================

-- 1. Create the database
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'DXSuite_TenantRegistry')
BEGIN
    CREATE DATABASE DXSuite_TenantRegistry COLLATE SQL_Latin1_General_CP1_CI_AS;
    PRINT 'Created database DXSuite_TenantRegistry';
END
GO

USE DXSuite_TenantRegistry;
GO

-- 2. Application login (least-privilege)
-- Replace <AppUserPassword> with a strong generated secret before running. The schema-level
-- GRANT (no DELETE) covers all current and migration-created tables. No DELETE is intentional:
-- deactivate rows (IsEnabled = 0, IsActive = 0) rather than hard-deleting.
IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = 'dxsuite_registry_app')
BEGIN
    CREATE LOGIN dxsuite_registry_app
        WITH PASSWORD = '<AppUserPassword>', CHECK_POLICY = ON, CHECK_EXPIRATION = ON;
    PRINT 'Created login dxsuite_registry_app';
END
GO

IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = 'dxsuite_registry_app')
BEGIN
    CREATE USER dxsuite_registry_app FOR LOGIN dxsuite_registry_app;
    GRANT SELECT, INSERT, UPDATE ON SCHEMA::dbo TO dxsuite_registry_app;
    PRINT 'Granted app user permissions (schema-level select/insert/update, no delete)';
END
GO

PRINT 'Tenant Registry DB bootstrap complete. Apply schema with: dotnet ef database update --context TenantRegistryDbContext';
GO
