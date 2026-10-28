-- =============================================================================
-- DXSuite Director — Application Login (test/dev only)
-- Creates the dxsuite_director_app login + user + role grants the application
-- needs to connect. Mirrors what director-provisioning-internal.sql does, so
-- the customer-facing director-provisioning.sql output can be tested end-to-end.
--
-- DO NOT SHIP THIS SCRIPT TO A CUSTOMER. The customer-facing flow expects
-- the DBA to create the service account per site policy (see the header of
-- director-provisioning.sql).
--
-- Before running:
--   * Change the password literal below.
--   * Change the USE statement below if your DB is not named DXSuite_Director
--     (e.g. DXSuite_Demo_Director).
-- =============================================================================

-- =============================================================================
-- 1. Server-scope login
-- =============================================================================
USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'dxsuite_director_app')
BEGIN
    CREATE LOGIN dxsuite_director_app
        WITH PASSWORD = '<AppUserPassword>',
             CHECK_POLICY = ON,
             CHECK_EXPIRATION = ON;
    PRINT 'Created login dxsuite_director_app';
END
ELSE
BEGIN
    PRINT 'Login dxsuite_director_app already exists - skipped';
END
GO

-- =============================================================================
-- 2. Database-scope user + role grants
-- =============================================================================
USE DXSuite_Director;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'dxsuite_director_app')
BEGIN
    CREATE USER dxsuite_director_app FOR LOGIN dxsuite_director_app;
    PRINT 'Created user dxsuite_director_app';
END
ELSE
BEGIN
    PRINT 'User dxsuite_director_app already exists - skipped';
END

ALTER ROLE db_datareader ADD MEMBER dxsuite_director_app;
ALTER ROLE db_datawriter ADD MEMBER dxsuite_director_app;
PRINT 'Granted db_datareader + db_datawriter to dxsuite_director_app';
GO

-- =============================================================================
-- 3. Verification
-- =============================================================================
SELECT name, type_desc, create_date
  FROM sys.server_principals
 WHERE name = 'dxsuite_director_app';

SELECT dp.name AS UserName,
       r.name  AS RoleName
  FROM sys.database_principals dp
  JOIN sys.database_role_members rm ON rm.member_principal_id = dp.principal_id
  JOIN sys.database_principals  r  ON r.principal_id = rm.role_principal_id
 WHERE dp.name = 'dxsuite_director_app';
GO
