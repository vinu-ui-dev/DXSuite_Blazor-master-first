-- =============================================================================
-- DXSuite Director — Seed Data (system lookup tables)
-- Run AFTER the schema is applied:
--     dotnet ef database update --project src/DXSuite.Director --context DirectorDbContext
-- Idempotent (every row checks existence first); safe to re-run.
--
-- These are system lookup rows the application expects to exist (roles, event
-- types and severities, device types, platforms, display/asset/location groups
-- and types, plus the ConstraintId = 0 no-constraint sentinel). EF migrations
-- own schema, not data, so this seed lives here rather than in a migration.
-- =============================================================================
USE DXSuite_Director;   -- adjust to the environment's Director database name
GO

-- =============================================================================
-- 6b. Constraint seed â€” ConstraintId 0 is the "no constraint" sentinel.
--     tblDisplay and tblAsset both default to ConstraintId = 0. EF Core treats
--     this as a required relationship (non-nullable FK), so without a matching
--     row the ORM JOIN eliminates all unconstrained displays and assets from
--     query results. This row must exist before any display or asset is created.
-- =============================================================================
SET IDENTITY_INSERT tblConstraint ON;
IF NOT EXISTS (SELECT 1 FROM tblConstraint WHERE ConstraintId = 0)
    INSERT INTO tblConstraint (ConstraintId, ConstraintName, IsGlobal, ResolutionMode)
    VALUES (0, 'None', 0, 'aspect-ratio');
SET IDENTITY_INSERT tblConstraint OFF;
DBCC CHECKIDENT ('tblConstraint', RESEED, 1);
PRINT 'Seeded tblConstraint sentinel (ConstraintId = 0)';
GO

-- =============================================================================
-- 22. Seed data â€” lookup tables
-- =============================================================================

-- â”€â”€ tblUserRole â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SET IDENTITY_INSERT tblUserRole ON;
IF NOT EXISTS (SELECT 1 FROM tblUserRole WHERE RoleId = 1) INSERT INTO tblUserRole (RoleId, RoleName, IsGlobal, IsSystem) VALUES (1, 'Admin', 1, 1);
IF NOT EXISTS (SELECT 1 FROM tblUserRole WHERE RoleId = 2) INSERT INTO tblUserRole (RoleId, RoleName, IsGlobal, IsSystem) VALUES (2, 'Architect', 1, 1);
IF NOT EXISTS (SELECT 1 FROM tblUserRole WHERE RoleId = 3) INSERT INTO tblUserRole (RoleId, RoleName, IsGlobal, IsSystem) VALUES (3, 'Contributor', 1, 1);
IF NOT EXISTS (SELECT 1 FROM tblUserRole WHERE RoleId = 4) INSERT INTO tblUserRole (RoleId, RoleName, IsGlobal, IsSystem) VALUES (4, 'NewUser', 1, 1);
SET IDENTITY_INSERT tblUserRole OFF;
DBCC CHECKIDENT ('tblUserRole', RESEED, 4);
PRINT 'Seeded tblUserRole';
GO

-- â”€â”€ tblEventSeverity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SET IDENTITY_INSERT tblEventSeverity ON;
IF NOT EXISTS (SELECT 1 FROM tblEventSeverity WHERE EventSeverityId = 1) INSERT INTO tblEventSeverity (EventSeverityId, SeverityName) VALUES (1, 'INFO');
IF NOT EXISTS (SELECT 1 FROM tblEventSeverity WHERE EventSeverityId = 2) INSERT INTO tblEventSeverity (EventSeverityId, SeverityName) VALUES (2, 'WARNING');
IF NOT EXISTS (SELECT 1 FROM tblEventSeverity WHERE EventSeverityId = 3) INSERT INTO tblEventSeverity (EventSeverityId, SeverityName) VALUES (3, 'ERROR');
IF NOT EXISTS (SELECT 1 FROM tblEventSeverity WHERE EventSeverityId = 4) INSERT INTO tblEventSeverity (EventSeverityId, SeverityName) VALUES (4, 'CRITICAL');
SET IDENTITY_INSERT tblEventSeverity OFF;
DBCC CHECKIDENT ('tblEventSeverity', RESEED, 4);
PRINT 'Seeded tblEventSeverity';
GO

-- â”€â”€ tblEventType â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SET IDENTITY_INSERT tblEventType ON;
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 1)  INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (1,  'DEVICE_REBOOT');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 2)  INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (2,  'DEVICE_UPDATE');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 3)  INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (3,  'AUTO_HEAL');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 4)  INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (4,  'CONTENT_HEALTH');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 5)  INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (5,  'DEVICE_OFFLINE');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 6)  INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (6,  'DEVICE_ONLINE');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 7)  INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (7,  'HDMI_DISCONNECT');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 8)  INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (8,  'HDMI_CONNECT');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 9)  INSERT INTO tblEventType (EventTypeId, EventTypeName, Description) VALUES (9,  'OVERRIDE_ACTIVATED',   'Content override was activated');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 10) INSERT INTO tblEventType (EventTypeId, EventTypeName, Description) VALUES (10, 'OVERRIDE_DEACTIVATED', 'Content override was deactivated');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 11) INSERT INTO tblEventType (EventTypeId, EventTypeName, Description) VALUES (11, 'UPDATE_STARTED',       'Device update started');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 12) INSERT INTO tblEventType (EventTypeId, EventTypeName, Description) VALUES (12, 'UPDATE_COMPLETE',      'Device update completed');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 13) INSERT INTO tblEventType (EventTypeId, EventTypeName, Description) VALUES (13, 'UPDATE_FAILED',        'Device update failed');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 14) INSERT INTO tblEventType (EventTypeId, EventTypeName, Description) VALUES (14, 'DOWNLOAD_COMPLETE',    'Update download completed');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 15) INSERT INTO tblEventType (EventTypeId, EventTypeName, Description) VALUES (15, 'ROLLBACK',             'Device rolled back to previous version');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 16) INSERT INTO tblEventType (EventTypeId, EventTypeName, Description) VALUES (16, 'VERSION_REPORTED',     'Device reported its version');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 17) INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (17, 'CONTENT_MODERATION_APPROVED');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 18) INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (18, 'CONTENT_MODERATION_FLAGGED');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 19) INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (19, 'CONTENT_MODERATION_SKIPPED');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 20) INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (20, 'CONTENT_MODERATION_ERROR');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 21) INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (21, 'CONTENT_SUBMITTED_FOR_APPROVAL');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 22) INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (22, 'MODERATION_APPROVED');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 23) INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (23, 'MODERATION_FLAGGED');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 24) INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (24, 'MODERATION_SKIPPED');
IF NOT EXISTS (SELECT 1 FROM tblEventType WHERE EventTypeId = 25) INSERT INTO tblEventType (EventTypeId, EventTypeName) VALUES (25, 'MODERATION_ERROR');
SET IDENTITY_INSERT tblEventType OFF;
DBCC CHECKIDENT ('tblEventType', RESEED, 25);
PRINT 'Seeded tblEventType';
GO

-- â”€â”€ tblDeviceType â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SET IDENTITY_INSERT tblDeviceType ON;
IF NOT EXISTS (SELECT 1 FROM tblDeviceType WHERE DeviceTypeId = 1) INSERT INTO tblDeviceType (DeviceTypeId, TypeName, Description, IsActive) VALUES (1, 'Player',     'DirectorClient media player device', 1);
IF NOT EXISTS (SELECT 1 FROM tblDeviceType WHERE DeviceTypeId = 2) INSERT INTO tblDeviceType (DeviceTypeId, TypeName, Description, IsActive) VALUES (2, 'Display',    'Display-only device (no player, RS-232 controlled)', 1);
IF NOT EXISTS (SELECT 1 FROM tblDeviceType WHERE DeviceTypeId = 3) INSERT INTO tblDeviceType (DeviceTypeId, TypeName, Description, IsActive) VALUES (3, 'BrightSign', 'BrightSign media player', 1);
SET IDENTITY_INSERT tblDeviceType OFF;
DBCC CHECKIDENT ('tblDeviceType', RESEED, 3);
PRINT 'Seeded tblDeviceType';
GO

-- â”€â”€ tblAppPlatform â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SET IDENTITY_INSERT tblAppPlatform ON;
IF NOT EXISTS (SELECT 1 FROM tblAppPlatform WHERE PlatformId = 1) INSERT INTO tblAppPlatform (PlatformId, PlatformName, PlatformDescription, FolderName, IsActive) VALUES (1, 'BrightSign', 'BrightSign digital signage players', 'BrightSign', 1);
IF NOT EXISTS (SELECT 1 FROM tblAppPlatform WHERE PlatformId = 2) INSERT INTO tblAppPlatform (PlatformId, PlatformName, PlatformDescription, FolderName, IsActive) VALUES (2, 'WebOS',      'LG WebOS signage displays',          'WebOS',      1);
IF NOT EXISTS (SELECT 1 FROM tblAppPlatform WHERE PlatformId = 3) INSERT INTO tblAppPlatform (PlatformId, PlatformName, PlatformDescription, FolderName, IsActive) VALUES (3, 'SignageOS',  'SignageOS cloud platform',            'SignageOS',  1);
IF NOT EXISTS (SELECT 1 FROM tblAppPlatform WHERE PlatformId = 4) INSERT INTO tblAppPlatform (PlatformId, PlatformName, PlatformDescription, FolderName, IsActive) VALUES (4, 'Windows',   'Windows desktop/embedded',            'Windows',    1);
IF NOT EXISTS (SELECT 1 FROM tblAppPlatform WHERE PlatformId = 5) INSERT INTO tblAppPlatform (PlatformId, PlatformName, PlatformDescription, FolderName, IsActive) VALUES (5, 'Mac',       'macOS desktop',                       'Mac',        1);
IF NOT EXISTS (SELECT 1 FROM tblAppPlatform WHERE PlatformId = 6) INSERT INTO tblAppPlatform (PlatformId, PlatformName, PlatformDescription, FolderName, IsActive) VALUES (6, 'Linux',     'Linux desktop/embedded',              'Linux',      1);
IF NOT EXISTS (SELECT 1 FROM tblAppPlatform WHERE PlatformId = 7) INSERT INTO tblAppPlatform (PlatformId, PlatformName, PlatformDescription, FolderName, IsActive) VALUES (7, 'Android',   'Android devices',                     'Android',    1);
SET IDENTITY_INSERT tblAppPlatform OFF;
DBCC CHECKIDENT ('tblAppPlatform', RESEED, 7);
PRINT 'Seeded tblAppPlatform';
GO

-- â”€â”€ tblDisplayGroup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
-- tblDisplay.DisplayGroupId defaults to 1; this row must exist.
SET IDENTITY_INSERT tblDisplayGroup ON;
IF NOT EXISTS (SELECT 1 FROM tblDisplayGroup WHERE DisplayGroupId = 1)
    INSERT INTO tblDisplayGroup (DisplayGroupId, DisplayGroupName, SortOrder) VALUES (1, 'Unassigned', 99);
SET IDENTITY_INSERT tblDisplayGroup OFF;
DBCC CHECKIDENT ('tblDisplayGroup', RESEED, 1);
PRINT 'Seeded tblDisplayGroup';
GO

-- â”€â”€ tblDisplayType â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
-- Starts at 0; tblDisplay.DisplayTypeId defaults to 0.
SET IDENTITY_INSERT tblDisplayType ON;
IF NOT EXISTS (SELECT 1 FROM tblDisplayType WHERE DisplayTypeId = 0) INSERT INTO tblDisplayType (DisplayTypeId, DisplayTypeName) VALUES (0, 'Standard Display');
IF NOT EXISTS (SELECT 1 FROM tblDisplayType WHERE DisplayTypeId = 1) INSERT INTO tblDisplayType (DisplayTypeId, DisplayTypeName) VALUES (1, 'Backwall Content');
IF NOT EXISTS (SELECT 1 FROM tblDisplayType WHERE DisplayTypeId = 2) INSERT INTO tblDisplayType (DisplayTypeId, DisplayTypeName) VALUES (2, 'Backwall Mask');
SET IDENTITY_INSERT tblDisplayType OFF;
DBCC CHECKIDENT ('tblDisplayType', RESEED, 2);
PRINT 'Seeded tblDisplayType';
GO

-- â”€â”€ tblAssetGroup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
-- tblAsset.AssetGroupId references this; seed a default group.
SET IDENTITY_INSERT tblAssetGroup ON;
IF NOT EXISTS (SELECT 1 FROM tblAssetGroup WHERE AssetGroupId = 1)
    INSERT INTO tblAssetGroup (AssetGroupId, AssetGroupName) VALUES (1, 'Default');
SET IDENTITY_INSERT tblAssetGroup OFF;
DBCC CHECKIDENT ('tblAssetGroup', RESEED, 1);
PRINT 'Seeded tblAssetGroup';
GO

-- â”€â”€ tblAssetType â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
-- Starts at 0 (Image = 0); tblAsset.AssetTypeId defaults to 0.
SET IDENTITY_INSERT tblAssetType ON;
IF NOT EXISTS (SELECT 1 FROM tblAssetType WHERE AssetTypeId = 0) INSERT INTO tblAssetType (AssetTypeId, AssetTypeName) VALUES (0, 'Image');
IF NOT EXISTS (SELECT 1 FROM tblAssetType WHERE AssetTypeId = 1) INSERT INTO tblAssetType (AssetTypeId, AssetTypeName) VALUES (1, 'Video');
IF NOT EXISTS (SELECT 1 FROM tblAssetType WHERE AssetTypeId = 2) INSERT INTO tblAssetType (AssetTypeId, AssetTypeName) VALUES (2, 'Watchlist');
IF NOT EXISTS (SELECT 1 FROM tblAssetType WHERE AssetTypeId = 3) INSERT INTO tblAssetType (AssetTypeId, AssetTypeName) VALUES (3, 'Map');
IF NOT EXISTS (SELECT 1 FROM tblAssetType WHERE AssetTypeId = 4) INSERT INTO tblAssetType (AssetTypeId, AssetTypeName) VALUES (4, 'Web');
IF NOT EXISTS (SELECT 1 FROM tblAssetType WHERE AssetTypeId = 5) INSERT INTO tblAssetType (AssetTypeId, AssetTypeName) VALUES (5, 'Stream');
SET IDENTITY_INSERT tblAssetType OFF;
DBCC CHECKIDENT ('tblAssetType', RESEED, 5);
PRINT 'Seeded tblAssetType';
GO

-- â”€â”€ tblLocationGroup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
-- Seed one default group; customers add their own via the admin UI.
SET IDENTITY_INSERT tblLocationGroup ON;
IF NOT EXISTS (SELECT 1 FROM tblLocationGroup WHERE LocationGroupId = 1)
    INSERT INTO tblLocationGroup (LocationGroupId, LocationGroupName, SortOrder) VALUES (1, 'General', 99);
SET IDENTITY_INSERT tblLocationGroup OFF;
DBCC CHECKIDENT ('tblLocationGroup', RESEED, 1);
PRINT 'Seeded tblLocationGroup';
GO

-- â”€â”€ tblLocationType â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
-- System location types. CBP-specific types excluded; customer-specific
-- types can be added via the admin UI post-install.
-- ComponentTypeName uses the DXSuite.Director namespace.
SET IDENTITY_INSERT tblLocationType ON;
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1, 'CheckIn Backwall', 1, 1, 'DXSuite.Director.Components.LocationTypes.Custom.CheckinBackwall', 1);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 3)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (3, 'Video Wall with Mask', 1, 1, NULL, 1);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 4)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (4, 'Card', 1, 0, 'DXSuite.Director.Components.LocationTypes.Card', 1);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 7)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (7, 'Playlist', 0, 0, 'DXSuite.Director.Components.LocationTypes.Playlist', 0);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 8)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (8, 'Airline Location Scheduler', 0, 1, 'DXSuite.Director.Components.LocationTypes.Custom.OW_Scheduler', 0);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 10)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (10, 'Concourse Entry Playlist', 0, 1, 'DXSuite.Director.Components.LocationTypes.Custom.ConcourseEntryPlaylist', 0);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 11)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (11, 'Welcome Messages', 0, 1, 'DXSuite.Director.Components.LocationTypes.Custom.WelcomeMessages', 0);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 12)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (12, 'TV Controller', 1, 1, 'DXSuite.Director.Components.LocationTypes.Custom.TVController', 0);
ELSE
    UPDATE tblLocationType
       SET LinkedToDisplay     = 1,
           ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Custom.TVController',
           ShowOverrideSection = 0
     WHERE LocationTypeId = 12
       AND (ComponentTypeName IS NULL OR ComponentTypeName <> 'DXSuite.Director.Components.LocationTypes.Custom.TVController');

-- NetJets Video Wall â€” customer-specific LocationType served by the DXSuite.NetJets module.
-- Hosts the message editor + Messages/FullScreenTv toggles + a Manage button that routes
-- to the linked TV Controller location (LocationId configured in appsettings
-- under NetJets:VideoWall:TvControllerLocationId).
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1100)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1100, 'NetJets Video Wall', 0, 1, 'DXSuite.NetJets.Components.LocationTypes.NetJetsVideoWall', 0);
ELSE
    UPDATE tblLocationType
       SET LinkedToDisplay     = 0,
           ComponentTypeName   = 'DXSuite.NetJets.Components.LocationTypes.NetJetsVideoWall',
           ShowOverrideSection = 0
     WHERE LocationTypeId = 1100
       AND (ComponentTypeName IS NULL OR ComponentTypeName <> 'DXSuite.NetJets.Components.LocationTypes.NetJetsVideoWall');

-- One-time NetJets location row creation (manual step in environments that deploy the NetJets module).
-- LocationGroupId must reference a real tblLocationGroup (any existing group is fine). Run once:
--
--   INSERT INTO tblLocation (LocationName, LocationTypeId, LocationGroupId, SortOrder)
--   VALUES ('NetJets Video Wall', 1100, <existingGroupId>, 99);
--
-- Then note the assigned LocationId; users browse to /location/{thatId}.
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1012)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1012, 'Override Control', 0, 0, NULL, 0);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1013)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1013, 'FloorPlan', 0, 1, NULL, 1);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1014)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1014, 'Zone Control', 1, 1, 'DXSuite.Director.Components.LocationTypes.Custom.ZoneVisualEditor', 1);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1015)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1015, 'Layout Location', 0, 0, 'DXSuite.Director.Components.LocationTypes.Custom.LayoutLocation', 1);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1016)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1016, 'PTZ Camera Control', 0, 0, 'DXSuite.Director.Components.LocationTypes.PtzCameraControl', 0);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1020)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1020, 'Asset Library', 0, 0, 'DXSuite.Director.Components.LocationTypes.AssetLibrary', 0);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1030)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1030, 'Panorama', 1, 0, 'DXSuite.Director.Components.LocationTypes.Panorama', 1);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 2020)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (2020, 'Floorplan', 0, 0, 'DXSuite.Director.Components.LocationTypes.FloorPlan', 0);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 2021)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (2021, 'Override Content', 0, 0, NULL, 0);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 2022)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (2022, 'VideoWall', 1, 1, 'DXSuite.Director.Components.LocationTypes.Custom.VideoWall', 1);
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 2023)
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (2023, 'AirlineClusterView', 1, 1, 'DXSuite.Director.Components.LocationTypes.Custom.AirlineClusterView', 0);
SET IDENTITY_INSERT tblLocationType OFF;
DBCC CHECKIDENT ('tblLocationType', RESEED, 2023);
PRINT 'Seeded tblLocationType';
GO

