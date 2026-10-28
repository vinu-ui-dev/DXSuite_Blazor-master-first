-- =============================================================================
-- DXSuite Director — Customer Seed Script
-- Run AFTER director-provisioning.sql against the DXSuite_Director database.
--
-- Populates:
--   tblLocationGroup : 2 groups (BrightSign Content, Moxie Content)
--   tblLocation      : 3 locations (TV Control, Video Wall TV, Video Wall Control)
--   tblDisplay       : 51 displays (DIGSIGN-001..050 + Video Wall TV)
--   tblUser          : 1 admin user (admin@dxsuite.local)
--   tblLocalAccount  : matching local-account login for the admin user
--
-- Bootstrap admin credentials (change before any non-trivial deployment):
--   Email    : admin@dxsuite.local
--   Password : DxsAdmin!2026
--   Role     : Admin (RoleId = 1)
--
-- Idempotent — safe to re-run; existing rows (matched by name/email) are not duplicated.
-- =============================================================================

USE DXSuite_Director;
GO

SET NOCOUNT ON;

-- =============================================================================
-- 1. Location Groups
-- =============================================================================
DECLARE @BrightSignGroupId int;
DECLARE @MoxieGroupId      int;

IF NOT EXISTS (SELECT 1 FROM tblLocationGroup WHERE LocationGroupName = 'BrightSign Content')
    INSERT INTO tblLocationGroup (LocationGroupName, SortOrder)
    VALUES ('BrightSign Content', 1);

IF NOT EXISTS (SELECT 1 FROM tblLocationGroup WHERE LocationGroupName = 'Moxie Content')
    INSERT INTO tblLocationGroup (LocationGroupName, SortOrder)
    VALUES ('Moxie Content', 2);

SELECT @BrightSignGroupId = LocationGroupId
  FROM tblLocationGroup
 WHERE LocationGroupName = 'BrightSign Content';

SELECT @MoxieGroupId = LocationGroupId
  FROM tblLocationGroup
 WHERE LocationGroupName = 'Moxie Content';

PRINT CONCAT('LocationGroup BrightSign Content = ', @BrightSignGroupId);
PRINT CONCAT('LocationGroup Moxie Content      = ', @MoxieGroupId);

-- =============================================================================
-- 2. Verify required LocationType rows exist (seeded by director-provisioning.sql)
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 12)
    THROW 50001, 'LocationTypeId 12 (TV Controller) is missing. Run director-provisioning.sql first.', 1;

IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeId = 1100)
    THROW 50002, 'LocationTypeId 1100 (NetJets Video Wall) is missing. Run director-provisioning.sql first.', 1;

-- =============================================================================
-- 3. Locations
-- =============================================================================
DECLARE @TVControlLocId        int;
DECLARE @VideoWallTVLocId      int;
DECLARE @VideoWallControlLocId int;

IF NOT EXISTS (
    SELECT 1 FROM tblLocation
     WHERE LocationName = 'TV Control'
       AND LocationGroupId = @BrightSignGroupId)
    INSERT INTO tblLocation (LocationName, LocationTypeId, LocationGroupId, SortOrder)
    VALUES ('TV Control', 12, @BrightSignGroupId, 1);

IF NOT EXISTS (
    SELECT 1 FROM tblLocation
     WHERE LocationName = 'Video Wall TV'
       AND LocationGroupId = @MoxieGroupId)
    INSERT INTO tblLocation (LocationName, LocationTypeId, LocationGroupId, SortOrder)
    VALUES ('Video Wall TV', 12, @MoxieGroupId, 2);

IF NOT EXISTS (
    SELECT 1 FROM tblLocation
     WHERE LocationName = 'Video Wall Control'
       AND LocationGroupId = @MoxieGroupId)
    INSERT INTO tblLocation (LocationName, LocationTypeId, LocationGroupId, SortOrder)
    VALUES ('Video Wall Control', 1100, @MoxieGroupId, 1);

SELECT @TVControlLocId = LocationId
  FROM tblLocation
 WHERE LocationName = 'TV Control'
   AND LocationGroupId = @BrightSignGroupId;

SELECT @VideoWallTVLocId = LocationId
  FROM tblLocation
 WHERE LocationName = 'Video Wall TV'
   AND LocationGroupId = @MoxieGroupId;

SELECT @VideoWallControlLocId = LocationId
  FROM tblLocation
 WHERE LocationName = 'Video Wall Control'
   AND LocationGroupId = @MoxieGroupId;

PRINT CONCAT('Location TV Control          = ', @TVControlLocId);
PRINT CONCAT('Location Video Wall TV       = ', @VideoWallTVLocId);
PRINT CONCAT('Location Video Wall Control  = ', @VideoWallControlLocId);

-- =============================================================================
-- 4. Displays — DIGSIGN-001..DIGSIGN-050 under TV Control
--    DisplayMode/Type/Group/Height/Width/Constraint/Enabled match column defaults
--    but are stated explicitly so the script is self-documenting.
-- =============================================================================
DECLARE @i    int = 1;
DECLARE @name varchar(50);

WHILE @i <= 50
BEGIN
    SET @name = 'DIGSIGN-' + RIGHT('000' + CAST(@i AS varchar(3)), 3);

    IF NOT EXISTS (SELECT 1 FROM tblDisplay WHERE DisplayName = @name)
        INSERT INTO tblDisplay (
            LocationId,
            DisplayName,
            DisplayMode,
            DisplayTypeId,
            DisplayGroupId,
            DisplayHeight,
            DisplayWidth,
            ConstraintId,
            Enabled,
            SortOrder)
        VALUES (
            @TVControlLocId,
            @name,
            0,           -- DisplayMode
            0,           -- DisplayTypeId (Standard Display)
            1,           -- DisplayGroupId (Unassigned)
            1080.0,      -- DisplayHeight
            1920.0,      -- DisplayWidth
            0,           -- ConstraintId
            1,           -- Enabled
            @i);         -- SortOrder

    SET @i = @i + 1;
END

PRINT 'Seeded DIGSIGN-001..DIGSIGN-050';

-- =============================================================================
-- 5. Display — Video Wall TV under the Video Wall TV location
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM tblDisplay WHERE DisplayName = 'Video Wall TV')
    INSERT INTO tblDisplay (
        LocationId,
        DisplayName,
        DisplayMode,
        DisplayTypeId,
        DisplayGroupId,
        DisplayHeight,
        DisplayWidth,
        ConstraintId,
        Enabled,
        SortOrder)
    VALUES (
        @VideoWallTVLocId,
        'Video Wall TV',
        0,            -- DisplayMode
        0,            -- DisplayTypeId
        1,            -- DisplayGroupId
        1080.0,       -- DisplayHeight
        1920.0,       -- DisplayWidth
        0,            -- ConstraintId
        1,            -- Enabled
        1);           -- SortOrder

PRINT 'Seeded display Video Wall TV';
GO

-- =============================================================================
-- 6. Bootstrap admin local account
--    Lets the application be logged into before Okta (or any external IdP) is
--    configured. Disable or delete once a real admin account exists.
--
--    PasswordHash was generated by Microsoft.AspNetCore.Cryptography.KeyDerivation
--    (PBKDF2-HMAC-SHA256, 600000 iterations, 16-byte salt, 32-byte hash) to match
--    LocalAccountService.HashPassword. To rotate the password, regenerate the
--    hash with the same algorithm and UPDATE tblLocalAccount.PasswordHash.
-- =============================================================================
DECLARE @AdminEmail           varchar(256) = 'admin@dxsuite.local';
DECLARE @AdminNormalizedEmail varchar(256) = 'ADMIN@DXSUITE.LOCAL';
DECLARE @AdminPasswordHash    varchar(512) = '600000.s/bUk7gwyZ/1gkeIEbkHhQ==.4h64poB7o9zrqaQ431gB+KAPe9TzQxAa9FT/oJYoTlY=';
DECLARE @AdminUserId          int;

IF NOT EXISTS (SELECT 1 FROM tblUser WHERE UserPrincipalName = @AdminEmail)
BEGIN
    INSERT INTO tblUser (UserPrincipalName, FriendlyName, GivenName, Surname, RoleId, IsDisabled, CreateDate)
    VALUES (@AdminEmail, 'Admin User', 'Admin', 'User', 1, 0, GETUTCDATE());
    PRINT CONCAT('Created tblUser ', @AdminEmail);
END
ELSE
    PRINT CONCAT('tblUser ', @AdminEmail, ' already exists - skipped');

SELECT @AdminUserId = UserId FROM tblUser WHERE UserPrincipalName = @AdminEmail;

IF NOT EXISTS (SELECT 1 FROM tblLocalAccount WHERE NormalizedEmail = @AdminNormalizedEmail)
BEGIN
    INSERT INTO tblLocalAccount (Email, NormalizedEmail, PasswordHash, EmailConfirmed, AccessFailedCount, UserId, CreateDate)
    VALUES (@AdminEmail, @AdminNormalizedEmail, @AdminPasswordHash, 1, 0, @AdminUserId, GETUTCDATE());
    PRINT 'Created tblLocalAccount for admin';
END
ELSE
    PRINT 'tblLocalAccount for admin already exists - skipped';
GO

-- =============================================================================
-- 7. Verification output
-- =============================================================================
SELECT 'tblLocationGroup' AS TableName, COUNT(*) AS [Rows] FROM tblLocationGroup
UNION ALL SELECT 'tblLocation',     COUNT(*) FROM tblLocation
UNION ALL SELECT 'tblDisplay',      COUNT(*) FROM tblDisplay
UNION ALL SELECT 'tblUser',         COUNT(*) FROM tblUser
UNION ALL SELECT 'tblLocalAccount', COUNT(*) FROM tblLocalAccount;

SELECT u.UserId, u.UserPrincipalName, r.RoleName, la.EmailConfirmed
  FROM tblUser            u
  JOIN tblUserRole        r  ON r.RoleId = u.RoleId
  LEFT JOIN tblLocalAccount la ON la.UserId = u.UserId
 WHERE u.UserPrincipalName = 'admin@dxsuite.local';

SELECT LocationGroupId, LocationGroupName, SortOrder
  FROM tblLocationGroup
 ORDER BY SortOrder;

SELECT l.LocationId, l.LocationName, lt.LocationTypeName, lg.LocationGroupName, l.SortOrder
  FROM tblLocation     l
  JOIN tblLocationType lt ON lt.LocationTypeId  = l.LocationTypeId
  JOIN tblLocationGroup lg ON lg.LocationGroupId = l.LocationGroupId
 ORDER BY lg.SortOrder, l.SortOrder;

SELECT d.DisplayId, d.DisplayName, l.LocationName, d.SortOrder
  FROM tblDisplay  d
  JOIN tblLocation l ON l.LocationId = d.LocationId
 ORDER BY l.LocationName, d.SortOrder;
GO
