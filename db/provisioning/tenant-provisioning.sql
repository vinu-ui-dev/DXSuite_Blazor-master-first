-- =============================================================================
-- DXSuite Tenant Registry — Provisioning Script
-- Database: DXSuite_TenantRegistry
-- Run this once when standing up a new DXSuite cloud deployment.
-- Run the "Add Tenant" section each time a new customer is onboarded.
-- =============================================================================

-- =============================================================================
-- 1. Create the registry database
-- =============================================================================
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'DXSuite_TenantRegistry')
BEGIN
    CREATE DATABASE DXSuite_TenantRegistry
        COLLATE SQL_Latin1_General_CP1_CI_AS;
    PRINT 'Created database DXSuite_TenantRegistry';
END
GO

USE DXSuite_TenantRegistry;
GO

-- =============================================================================
-- 2. Create tblTenant
-- =============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblTenant')
BEGIN
    CREATE TABLE tblTenant (
        TenantId                  nvarchar(100)  NOT NULL PRIMARY KEY,
        Subdomain                 nvarchar(100)  NOT NULL,
        CustomerCode              nvarchar(50)   NOT NULL,
        DisplayName               nvarchar(200)  NOT NULL,
        IsEnabled                 bit            NOT NULL DEFAULT 1,

        -- Connection strings (encrypt at rest via Always Encrypted or Key Vault references)
        ConnectionString_Director nvarchar(1000) NULL,
        ConnectionString_Sentinel nvarchar(1000) NULL,
        ConnectionString_CoreData nvarchar(1000) NULL,
        ConnectionString_Aviation nvarchar(1000) NULL,

        -- JSON config columns
        AuthConfigJson            nvarchar(max)  NULL,
        ModulesJson               nvarchar(max)  NULL,
        FeaturesJson              nvarchar(max)  NULL,
        SmtpConfigJson            nvarchar(max)  NULL,

        -- Media and limits
        FilePaths_MediaRoot       nvarchar(500)  NULL,
        MaxUsers                  int            NULL,

        -- Audit
        CreatedDate               datetime2      NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate              datetime2      NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE UNIQUE INDEX UX_tblTenant_Subdomain ON tblTenant (Subdomain);

    PRINT 'Created tblTenant';
END
GO

-- =============================================================================
-- 3. Create tblApiKeyMapping
-- =============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblApiKeyMapping')
BEGIN
    CREATE TABLE tblApiKeyMapping (
        ApiKeyMappingId int            NOT NULL IDENTITY(1,1) PRIMARY KEY,
        ApiKeyHash      nvarchar(64)   NOT NULL,   -- SHA-256 Base64 (44 chars), same value stored in tenant Director DB
        TenantId        nvarchar(100)  NOT NULL,
        IsActive        bit            NOT NULL DEFAULT 1,
        CreatedDate     datetime2      NOT NULL DEFAULT GETUTCDATE()
    );

    -- Unique index enables O(1) tenant resolution from an API key hash.
    CREATE UNIQUE INDEX UX_tblApiKeyMapping_Hash ON tblApiKeyMapping (ApiKeyHash) WHERE IsActive = 1;
    CREATE INDEX IX_tblApiKeyMapping_TenantId   ON tblApiKeyMapping (TenantId);

    PRINT 'Created tblApiKeyMapping';
END
GO

-- =============================================================================
-- 4. Application login (least-privilege)
-- Replace <AppUserPassword> with a strong generated secret before running.
-- =============================================================================
IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = 'dxsuite_registry_app')
BEGIN
    CREATE LOGIN dxsuite_registry_app
        WITH PASSWORD = '<AppUserPassword>',
             CHECK_POLICY = ON,
             CHECK_EXPIRATION = ON;
    PRINT 'Created login dxsuite_registry_app';
END
GO

IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = 'dxsuite_registry_app')
BEGIN
    CREATE USER dxsuite_registry_app FOR LOGIN dxsuite_registry_app;
    GRANT SELECT, INSERT, UPDATE ON tblTenant       TO dxsuite_registry_app;
    GRANT SELECT, INSERT, UPDATE ON tblApiKeyMapping TO dxsuite_registry_app;
    -- No DELETE: deactivate rows (IsEnabled = 0, IsActive = 0) rather than hard-deleting.
    PRINT 'Granted app user permissions';
END
GO

-- =============================================================================
-- 5. Add a tenant (run this section for each new customer)
-- Replace the placeholder values before executing.
-- =============================================================================

/*
USE DXSuite_TenantRegistry;

DECLARE @TenantId      nvarchar(100) = LOWER(NEWID());  -- or supply a fixed value
DECLARE @Subdomain     nvarchar(100) = 'acme';
DECLARE @CustomerCode  nvarchar(50)  = 'ACME';
DECLARE @DisplayName   nvarchar(200) = 'Acme Corp';
DECLARE @DirConnStr    nvarchar(1000) = 'Server=sql.dxsuite.internal;Database=DXSuite_ACME;...';
DECLARE @DirectorDbName sysname      = 'DXSuite_ACME';  -- used below to seed the Director database

-- Auth config: single Microsoft OIDC provider
DECLARE @AuthConfig nvarchar(max) = N'{
    "Provider": "microsoft",
    "Providers": {
        "Microsoft": {
            "Enabled": true,
            "DisplayName": "Acme Microsoft",
            "ClientId": "<azure-app-client-id>",
            "ClientSecret": "<azure-app-client-secret>",
            "TenantId": "<azure-tenant-id>",
            "CallbackPath": "/signin-oidc-acme-microsoft"
        }
    }
}';

-- Enabled modules
DECLARE @Modules nvarchar(max) = N'{"Director":true,"CoreData":false,"Aviation":false,"Sentinel":false,"Pulse":false}';

-- Feature flags
DECLARE @Features nvarchar(max) = N'{"Engage":false,"Wayfinding":false,"ContentPackage":false,"GroupDiscovery":false,"DomainGroupAssignment":false}';

INSERT INTO tblTenant
    (TenantId, Subdomain, CustomerCode, DisplayName, IsEnabled,
     ConnectionString_Director, AuthConfigJson, ModulesJson, FeaturesJson)
VALUES
    (@TenantId, @Subdomain, @CustomerCode, @DisplayName, 1,
     @DirConnStr, @AuthConfig, @Modules, @Features);

PRINT 'Inserted tenant ' + @Subdomain + ' with TenantId ' + @TenantId;
*/

-- =============================================================================
-- 5a. Seed the tenant Director database
-- Switches to the Director database declared above and seeds location types
-- and the video wall subsystem tables. Idempotent — safe to re-run.
-- =============================================================================

/*
EXEC('
USE [' + @DirectorDbName + '];

-- ── Location type: VideoWall ─────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeName = ''VideoWall'')
BEGIN
    INSERT INTO tblLocationType (LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection, CreateDate)
    VALUES (''VideoWall'', 1, 1, ''DXSuite.Director.Components.LocationTypes.Custom.VideoWall'', 1, GETUTCDATE());
    PRINT ''Seeded tblLocationType: VideoWall'';
END

-- ── Location type: AirlineClusterView ────────────────────────────────────────
-- ShowOverrideSection = 0: airline users interact via the scheduler, not direct override.
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeName = ''AirlineClusterView'')
BEGIN
    INSERT INTO tblLocationType (LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection, CreateDate)
    VALUES (''AirlineClusterView'', 1, 1, ''DXSuite.Director.Components.LocationTypes.Custom.AirlineClusterView'', 0, GETUTCDATE());
    PRINT ''Seeded tblLocationType: AirlineClusterView'';
END

-- ── tblUserGroupLocationDefault ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = ''tblUserGroupLocationDefault'')
BEGIN
    CREATE TABLE tblUserGroupLocationDefault (
        UserGroupLocationDefaultId  int          NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblUserGroupLocationDefault PRIMARY KEY,
        UserGroupId                 int          NOT NULL
            CONSTRAINT FK_tblUserGroupLocationDefault_UserGroup
                REFERENCES tblUserGroup(UserGroupId) ON DELETE CASCADE,
        LocationId                  int          NOT NULL
            CONSTRAINT FK_tblUserGroupLocationDefault_Location
                REFERENCES tblLocation(LocationId) ON DELETE CASCADE,
        DefaultConstraintId         int          NOT NULL
            CONSTRAINT FK_tblUserGroupLocationDefault_Constraint
                REFERENCES tblConstraint(ConstraintId) ON DELETE NO ACTION,
        BackgroundMode              nvarchar(20) NOT NULL
            CONSTRAINT DF_tblUserGroupLocationDefault_BackgroundMode DEFAULT (''fill-width''),
        CreateDate                  datetime2    NOT NULL
            CONSTRAINT DF_tblUserGroupLocationDefault_CreateDate DEFAULT (GETUTCDATE()),
        LastEditDate                datetime2    NOT NULL
            CONSTRAINT DF_tblUserGroupLocationDefault_LastEditDate DEFAULT (GETUTCDATE())
    );
    CREATE UNIQUE INDEX UX_tblUserGroupLocationDefault_GroupLocation
        ON tblUserGroupLocationDefault (UserGroupId, LocationId);
    PRINT ''Created tblUserGroupLocationDefault'';
END

-- ── tblScheduleEvent extensions ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(''tblScheduleEvent'') AND name = ''UserGroupId'')
BEGIN
    ALTER TABLE tblScheduleEvent ADD UserGroupId int NULL;
    ALTER TABLE tblScheduleEvent ADD
        CONSTRAINT FK_tblScheduleEvent_UserGroup
        FOREIGN KEY (UserGroupId) REFERENCES tblUserGroup(UserGroupId) ON DELETE SET NULL;
    PRINT ''Added tblScheduleEvent.UserGroupId'';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(''tblScheduleEvent'') AND name = ''OverrideConstraintId'')
BEGIN
    ALTER TABLE tblScheduleEvent ADD OverrideConstraintId int NULL;
    ALTER TABLE tblScheduleEvent ADD
        CONSTRAINT FK_tblScheduleEvent_OverrideConstraint
        FOREIGN KEY (OverrideConstraintId) REFERENCES tblConstraint(ConstraintId) ON DELETE SET NULL;
    PRINT ''Added tblScheduleEvent.OverrideConstraintId'';
END

UPDATE se
SET    se.UserGroupId = ugm.UserGroupId
FROM   tblScheduleEvent se
INNER JOIN tblUserGroupMetadata ugm
    ON ugm.MetadataKey   = ''AirlineCode''
   AND ugm.MetadataValue = se.AirlineCode
WHERE  se.AirlineCode IS NOT NULL
  AND  se.UserGroupId  IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = ''IX_tblScheduleEvent_UserGroupId'' AND object_id = OBJECT_ID(''tblScheduleEvent''))
BEGIN
    CREATE INDEX IX_tblScheduleEvent_UserGroupId
        ON tblScheduleEvent (UserGroupId)
        WHERE UserGroupId IS NOT NULL;
    PRINT ''Added index IX_tblScheduleEvent_UserGroupId'';
END

-- ── tblLocation cluster columns ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(''tblLocation'') AND name = ''IsTransientCluster'')
BEGIN
    ALTER TABLE tblLocation ADD IsTransientCluster bit NOT NULL
        CONSTRAINT DF_tblLocation_IsTransientCluster DEFAULT 0;
    PRINT ''Added tblLocation.IsTransientCluster'';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(''tblLocation'') AND name = ''ParentVideoWallLocationId'')
BEGIN
    ALTER TABLE tblLocation ADD ParentVideoWallLocationId int NULL;
    ALTER TABLE tblLocation ADD
        CONSTRAINT FK_tblLocation_ParentVideoWall
        FOREIGN KEY (ParentVideoWallLocationId) REFERENCES tblLocation(LocationId) ON DELETE NO ACTION;
    PRINT ''Added tblLocation.ParentVideoWallLocationId'';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(''tblLocation'') AND name = ''ClusterKey'')
BEGIN
    ALTER TABLE tblLocation ADD ClusterKey varchar(100) NULL;
    PRINT ''Added tblLocation.ClusterKey'';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(''tblLocation'') AND name = ''OwningUserGroupId'')
BEGIN
    ALTER TABLE tblLocation ADD OwningUserGroupId int NULL;
    ALTER TABLE tblLocation ADD
        CONSTRAINT FK_tblLocation_OwningUserGroup
        FOREIGN KEY (OwningUserGroupId) REFERENCES tblUserGroup(UserGroupId) ON DELETE SET NULL;
    PRINT ''Added tblLocation.OwningUserGroupId'';
END

-- ── tblWallLayoutMapping ──────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = ''tblWallLayoutMapping'')
BEGIN
    CREATE TABLE tblWallLayoutMapping (
        WallLayoutMappingId int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblWallLayoutMapping PRIMARY KEY,
        EndpointId          int           NOT NULL
            CONSTRAINT FK_tblWallLayoutMapping_Endpoint
                REFERENCES tblEndpoint(EndpointId) ON DELETE CASCADE,
        DisplayId           int           NOT NULL
            CONSTRAINT FK_tblWallLayoutMapping_Display
                REFERENCES tblDisplay(DisplayId) ON DELETE CASCADE,
        LayerRole           varchar(20)   NOT NULL
            CONSTRAINT DF_tblWallLayoutMapping_LayerRole DEFAULT (''status''),
        PlatformData        nvarchar(max) NULL,
        CreateDate          datetime2     NOT NULL
            CONSTRAINT DF_tblWallLayoutMapping_CreateDate DEFAULT (GETUTCDATE()),
        LastEditDate        datetime2     NOT NULL
            CONSTRAINT DF_tblWallLayoutMapping_LastEditDate DEFAULT (GETUTCDATE())
    );
    CREATE INDEX IX_tblWallLayoutMapping_Endpoint ON tblWallLayoutMapping (EndpointId);
    CREATE INDEX IX_tblWallLayoutMapping_Display   ON tblWallLayoutMapping (DisplayId);
    PRINT ''Created tblWallLayoutMapping'';
END

PRINT ''Director database seeding complete for '' + DB_NAME();
');
*/

-- =============================================================================
-- 6. Register an API key mapping (run after generating a key in the tenant app)
-- @ApiKeyHash is the SHA-256 hex returned by the Director API key creation endpoint.
-- =============================================================================

/*
INSERT INTO tblApiKeyMapping (ApiKeyHash, TenantId)
VALUES ('<sha256-hex-of-key>', '<tenant-id>');
*/

PRINT 'Provisioning script complete.';
GO
