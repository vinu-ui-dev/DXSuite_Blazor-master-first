-- ============================================================
-- DXSuite Migration Patch 2: SLC_DirectorDEV
-- Populates ComponentTypeName and ShowOverrideSection for
-- existing tblLocationType rows that predate this column.
-- Only updates rows where ComponentTypeName is still NULL
-- to avoid overwriting any manually configured values.
-- Safe to re-run.
-- ============================================================

USE SLC_DirectorDEV;
GO

PRINT '>> Populating tblLocationType.ComponentTypeName...';

-- LocationTypeId 1 — CheckIn Backwall
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Custom.CheckinBackwall',
    ShowOverrideSection = 1
WHERE LocationTypeId = 1 AND ComponentTypeName IS NULL;

-- LocationTypeId 3 — Video Wall with Mask (no component — intentionally NULL)
UPDATE tblLocationType SET
    ShowOverrideSection = 1
WHERE LocationTypeId = 3 AND ComponentTypeName IS NULL;

-- LocationTypeId 4 — Card
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Card',
    ShowOverrideSection = 1
WHERE LocationTypeId = 4 AND ComponentTypeName IS NULL;

-- LocationTypeId 7 — Playlist
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Playlist',
    ShowOverrideSection = 0
WHERE LocationTypeId = 7 AND ComponentTypeName IS NULL;

-- LocationTypeId 8 — Airline Location Scheduler
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Custom.OW_Scheduler',
    ShowOverrideSection = 0
WHERE LocationTypeId = 8 AND ComponentTypeName IS NULL;

-- LocationTypeId 10 — Concourse Entry Playlist
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Custom.ConcourseEntryPlaylist',
    ShowOverrideSection = 0
WHERE LocationTypeId = 10 AND ComponentTypeName IS NULL;

-- LocationTypeId 11 — Welcome Messages
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Custom.WelcomeMessages',
    ShowOverrideSection = 0
WHERE LocationTypeId = 11 AND ComponentTypeName IS NULL;

-- LocationTypeId 12 — TV Controller
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Custom.TVController',
    ShowOverrideSection = 0
WHERE LocationTypeId = 12 AND ComponentTypeName IS NULL;

-- LocationTypeId 1012 — Override Control (no component — intentionally NULL)
UPDATE tblLocationType SET
    ShowOverrideSection = 0
WHERE LocationTypeId = 1012 AND ComponentTypeName IS NULL;

-- LocationTypeId 1013 — FloorPlan (no component — intentionally NULL)
UPDATE tblLocationType SET
    ShowOverrideSection = 1
WHERE LocationTypeId = 1013 AND ComponentTypeName IS NULL;

-- LocationTypeId 1014 — Zone Control
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Custom.ZoneVisualEditor',
    ShowOverrideSection = 1
WHERE LocationTypeId = 1014 AND ComponentTypeName IS NULL;

-- LocationTypeId 1015 — Layout Location
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Custom.LayoutLocation',
    ShowOverrideSection = 1
WHERE LocationTypeId = 1015 AND ComponentTypeName IS NULL;

-- LocationTypeId 1016 — PTZ Camera Control
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.PtzCameraControl',
    ShowOverrideSection = 0
WHERE LocationTypeId = 1016 AND ComponentTypeName IS NULL;

-- LocationTypeId 1020 — Asset Library
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.AssetLibrary',
    ShowOverrideSection = 0
WHERE LocationTypeId = 1020 AND ComponentTypeName IS NULL;

-- LocationTypeId 1030 — Panorama
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Panorama',
    ShowOverrideSection = 1
WHERE LocationTypeId = 1030 AND ComponentTypeName IS NULL;

-- LocationTypeId 2020 — Floorplan (newer ID for full floor plan type)
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.FloorPlan',
    ShowOverrideSection = 0
WHERE LocationTypeId = 2020 AND ComponentTypeName IS NULL;

-- LocationTypeId 2021 — Override Content (no component — intentionally NULL)
UPDATE tblLocationType SET
    ShowOverrideSection = 0
WHERE LocationTypeId = 2021 AND ComponentTypeName IS NULL;

-- LocationTypeId 2022 — VideoWall
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Custom.VideoWall',
    ShowOverrideSection = 1
WHERE LocationTypeId = 2022 AND ComponentTypeName IS NULL;

-- LocationTypeId 2023 — AirlineClusterView
UPDATE tblLocationType SET
    ComponentTypeName   = 'DXSuite.Director.Components.LocationTypes.Custom.AirlineClusterView',
    ShowOverrideSection = 0
WHERE LocationTypeId = 2023 AND ComponentTypeName IS NULL;

PRINT CAST(@@ROWCOUNT AS varchar) + ' row(s) updated in final batch';

-- Verify — shows all types with their resolved component name
PRINT '';
PRINT '>> Current tblLocationType state:';
SELECT
    LocationTypeId,
    LocationTypeName,
    ComponentTypeName,
    ShowOverrideSection
FROM tblLocationType
ORDER BY LocationTypeId;
GO

PRINT '============================================================';
PRINT 'Patch 2 complete. Review the SELECT output above.';
PRINT 'Any rows with unexpected NULL ComponentTypeName are either';
PRINT 'intentional (Override Control, Video Wall with Mask, etc.)';
PRINT 'or custom types not in the standard mapping.';
PRINT '============================================================';
