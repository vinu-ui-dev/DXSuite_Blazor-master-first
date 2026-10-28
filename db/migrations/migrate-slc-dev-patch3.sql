-- ============================================================
-- DXSuite Migration Patch 3: SLC_DirectorDEV
-- Adds missing standard tblLocationType rows.
-- Uses SET IDENTITY_INSERT to preserve canonical IDs.
-- All inserts are idempotent (IF NOT EXISTS by LocationTypeId).
-- Existing rows are not modified or removed.
--
-- NOTE: VideoWall (2022), AirlineClusterView (2023), and
-- NetJets Video Wall (1100) are intentionally excluded:
--   - VideoWall / AirlineClusterView already exist at IDs
--     1015 / 1016 from the earlier patch.
--   - NetJets is not deployed at this site.
-- ============================================================

USE SLC_DirectorDEV;
GO

SET IDENTITY_INSERT tblLocationType ON;
GO

-- LocationTypeId 1020: Asset Library
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1020)
BEGIN
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1020, 'Asset Library', 0, 0, 'DXSuite.Director.Components.LocationTypes.AssetLibrary', 0);
    PRINT 'Inserted: Asset Library (1020)';
END
GO

-- LocationTypeId 1030: Panorama
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1030)
BEGIN
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (1030, 'Panorama', 1, 0, 'DXSuite.Director.Components.LocationTypes.Panorama', 1);
    PRINT 'Inserted: Panorama (1030)';
END
GO

-- LocationTypeId 2020: Floorplan
-- The UI create workflow (CreateLayoutModal.razor) looks up by
-- LocationTypeName = 'Floorplan' (lowercase p). The existing ID 1013
-- row is named 'FloorPlan' (uppercase P) and has no ComponentTypeName,
-- so this row is required for both creation and rendering to work.
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 2020)
BEGIN
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (2020, 'Floorplan', 0, 0, 'DXSuite.Director.Components.LocationTypes.FloorPlan', 0);
    PRINT 'Inserted: Floorplan (2020)';
END
GO

-- LocationTypeId 2021: Override Content
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 2021)
BEGIN
    INSERT INTO tblLocationType (LocationTypeId, LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection)
    VALUES (2021, 'Override Content', 0, 0, NULL, 0);
    PRINT 'Inserted: Override Content (2021)';
END
GO

SET IDENTITY_INSERT tblLocationType OFF;
GO

-- Reseed identity to the current max so future inserts don't collide
DBCC CHECKIDENT ('tblLocationType', RESEED);
GO

-- Verify
SELECT LocationTypeId, LocationTypeName, ComponentTypeName, ShowOverrideSection
FROM tblLocationType
ORDER BY LocationTypeId;
GO

PRINT '============================================================';
PRINT 'Patch 3 complete.';
PRINT '============================================================';
