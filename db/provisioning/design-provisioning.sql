-- =============================================================================
-- DXSuite Design DB — Bootstrap Script (database + login only)
-- Database: DXSuite_Design  (or per-tenant: DXSuite_Design_<CustomerCode>)
--
-- SCHEMA IS OWNED BY EF MIGRATIONS. This script creates only the database and the
-- least-privilege application login. Apply the schema with:
--     dotnet ef database update --project src/DXSuite.Design --context DesignDbContext
-- (or the tenant migrator, once it exists). Do not add table DDL here; add an EF
-- migration instead. See db/schema-migration-strategy.md.
-- =============================================================================

-- 1. Create the database
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'DXSuite_Design')
BEGIN
    CREATE DATABASE DXSuite_Design COLLATE SQL_Latin1_General_CP1_CI_AS;
    PRINT 'Created database DXSuite_Design';
END
GO

USE DXSuite_Design;
GO

-- 2. Application login (least-privilege)
-- Replace <AppUserPassword> with a strong generated secret before running.
IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = 'dxsuite_design_app')
BEGIN
    CREATE LOGIN dxsuite_design_app
        WITH PASSWORD = '<AppUserPassword>', CHECK_POLICY = ON, CHECK_EXPIRATION = ON;
    PRINT 'Created login dxsuite_design_app';
END
GO

IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = 'dxsuite_design_app')
BEGIN
    CREATE USER dxsuite_design_app FOR LOGIN dxsuite_design_app;
    EXEC sp_addrolemember 'db_datareader', 'dxsuite_design_app';
    EXEC sp_addrolemember 'db_datawriter', 'dxsuite_design_app';
    PRINT 'Granted app user permissions';
END
GO

PRINT 'Design DB bootstrap complete. Apply schema with: dotnet ef database update --context DesignDbContext';
GO
