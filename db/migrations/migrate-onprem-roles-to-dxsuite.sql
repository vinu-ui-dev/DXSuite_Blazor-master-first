/*
    Migrate on-prem role schema to the canonical DXSuite role set.

    Old (legacy, airport/airline) roles               Canonical DXSuite roles (tblUserRole)
    ------------------------------------               -------------------------------------
      1 Airport Admin       (IsGlobal=1)                 1 Admin        (IsGlobal=1)
      2 Airport Contributer (IsGlobal=1)                 2 Architect    (IsGlobal=1)
      3 Airport User        (IsGlobal=1)                 3 Contributor  (IsGlobal=1)
      4 Airline Admin       (IsGlobal=0)                 4 NewUser      (IsGlobal=1)
      5 Airline Contributer (IsGlobal=0)
      6 Airline User        (IsGlobal=0)
      7 Global Admin        (IsGlobal=1)
      8 New User            (IsGlobal=0)

    User-role remap policy ("admins stay admins, everyone else becomes a contributor"):
      1 Airport Admin       -> 1 Admin
      4 Airline Admin       -> 1 Admin
      7 Global Admin        -> 1 Admin
      2 Airport Contributer -> 3 Contributor
      3 Airport User        -> 3 Contributor
      5 Airline Contributer -> 3 Contributor
      6 Airline User        -> 3 Contributor
      8 New User            -> 4 NewUser   (pending-assignment state preserved)

    The Architect role (2) is created but assigned to no one, matching the requested policy.

    FK note: FK_tblUser_tblUserRole (tblUser.RoleId -> tblUserRole.RoleId) is enforced
    throughout. Users are remapped FIRST (every target RoleId 1/3/4 already exists), so
    legacy rows 5-8 carry no references when they are deleted. RoleIds 1-4 are renamed
    in place, never deleted, so no IDENTITY_INSERT is required.
*/

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 1) Remap users by their LEGACY role meaning, while legacy rows still exist.
UPDATE tblUser
SET RoleId = CASE RoleId
        WHEN 1 THEN 1   -- Airport Admin       -> Admin
        WHEN 4 THEN 1   -- Airline Admin       -> Admin
        WHEN 7 THEN 1   -- Global Admin        -> Admin
        WHEN 2 THEN 3   -- Airport Contributer -> Contributor
        WHEN 3 THEN 3   -- Airport User        -> Contributor
        WHEN 5 THEN 3   -- Airline Contributer -> Contributor
        WHEN 6 THEN 3   -- Airline User        -> Contributor
        WHEN 8 THEN 4   -- New User            -> NewUser
        ELSE RoleId
    END
WHERE RoleId IN (1, 2, 3, 4, 5, 6, 7, 8);

-- 2) Rename the four retained rows to the canonical DXSuite roles.
UPDATE tblUserRole SET RoleName = 'Admin',       RoleDescription = NULL, IsGlobal = 1 WHERE RoleId = 1;
UPDATE tblUserRole SET RoleName = 'Architect',   RoleDescription = NULL, IsGlobal = 1 WHERE RoleId = 2;
UPDATE tblUserRole SET RoleName = 'Contributor', RoleDescription = NULL, IsGlobal = 1 WHERE RoleId = 3;
UPDATE tblUserRole SET RoleName = 'NewUser',     RoleDescription = NULL, IsGlobal = 1 WHERE RoleId = 4;

-- 3) Drop the now-unreferenced legacy roles.
DELETE FROM tblUserRole WHERE RoleId IN (5, 6, 7, 8);

-- Safety check: no user may reference a role outside the canonical set.
IF EXISTS (SELECT 1 FROM tblUser WHERE RoleId NOT IN (1, 2, 3, 4))
BEGIN
    RAISERROR('Migration aborted: tblUser still references a non-canonical RoleId.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

COMMIT TRANSACTION;

-- Verification (run after commit):
-- SELECT RoleId, RoleName, RoleDescription, IsGlobal FROM tblUserRole ORDER BY RoleId;
-- SELECT r.RoleName, COUNT(*) AS UserCount
-- FROM tblUser u JOIN tblUserRole r ON u.RoleId = r.RoleId
-- GROUP BY r.RoleName ORDER BY r.RoleName;
