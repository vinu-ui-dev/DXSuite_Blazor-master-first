-- =============================================================================
-- LOCAL seed (data, not schema). Dev environment only.
--
-- Enriches the LG and Samsung Engage descriptors so DirectorClient content telemetry
-- (parsed by EngageTelemetryParser from TblDevice.ExtendedData into the heartbeat source
-- state) drives the evaluation pipeline:
--   * monitorPoints document the content signals (content health, proof-of-play age,
--     JS/console errors, foreground input, screenshot age) for the UI.
--   * commands give the Analyst engine (Tier-2 LLM) real remediations to recommend
--     (refresh-content, relaunch-app, reboot).
--   * healthRules stays EMPTY on purpose: with no Tier-1 rule, a player showing >1
--     content alert routes to Tier-2 (AI diagnosis) rather than Tier-1/Tier-3.
--
-- Also assigns the Samsung players (which currently have no descriptor version, so the
-- evaluator skips them) to the samsung descriptor. LG players already use version 1,
-- which this updates in place.
--
-- Idempotent: re-running refreshes the JSON and re-assigns only unassigned Samsung rows.
-- Do NOT run against production.
-- =============================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

DECLARE @now datetime = GETUTCDATE();

-- Shared monitorPoints + commands body; only deviceType/displayName/manufacturer differ per make.
DECLARE @lgJson varchar(max) = '{"descriptorVersion":"1.0","deviceType":"lg","displayName":"LG WebOS (Engage)","manufacturer":"LG","category":"display","communication":{"protocol":"director-heartbeat","defaultPort":null,"authentication":{"type":"none","required":false},"connectionType":"cloud","heartbeatMethod":{"type":"poll","command":null,"intervalSeconds":30}},"monitorPoints":[{"id":"web-content-status","name":"Web Content Status","category":"content","dataType":"string","description":"Health of a monitored web page (iframe running content-health.js): healthy, unresponsive, or failed. Only active while web content is playing."},{"id":"web-content-health-age","name":"Web Content Health Age","category":"content","dataType":"string","description":"Time since the monitored web page last sent a health heartbeat (informational)"},{"id":"web-content-js-errors","name":"Web Content JS Errors","category":"content","dataType":"number","description":"JavaScript errors reported by the monitored web page"},{"id":"input-status","name":"Display Input","category":"display","dataType":"string","description":"Whether the player app is in the foreground"},{"id":"console-errors","name":"Console Errors","category":"diagnostics","dataType":"number","description":"Error-level entries in the recent console log"},{"id":"screenshot-age","name":"Screenshot Age","category":"display","dataType":"string","description":"Age of the most recent device screenshot"}],"commands":[{"id":"refresh-content","name":"Refresh Content","category":"content","riskLevel":"low","requiresApproval":false,"protocol":null,"command":"","parameters":null,"timeoutMs":30000},{"id":"relaunch-app","name":"Relaunch Player App","category":"content","riskLevel":"medium","requiresApproval":false,"protocol":null,"command":"","parameters":null,"timeoutMs":60000},{"id":"reboot","name":"Reboot Device","category":"power","riskLevel":"high","requiresApproval":true,"protocol":null,"command":"","parameters":null,"timeoutMs":120000}],"healthRules":[]}';

DECLARE @smJson varchar(max) = REPLACE(REPLACE(REPLACE(@lgJson,
    '"deviceType":"lg"', '"deviceType":"samsung"'),
    '"displayName":"LG WebOS (Engage)"', '"displayName":"Samsung Tizen (Engage)"'),
    '"manufacturer":"LG"', '"manufacturer":"Samsung"');

-- 1) LG: update the active version JSON in place (players already point at it).
DECLARE @lgDescId int = (SELECT DescriptorID FROM SentinelDeviceDescriptor WHERE DeviceType = 'lg');
IF @lgDescId IS NOT NULL
BEGIN
    UPDATE SentinelDeviceDescriptorVersion SET DescriptorJson = @lgJson
    WHERE DescriptorId = @lgDescId AND IsActive = 1;
    PRINT 'LG descriptor version updated.';
END

-- 2) Samsung: ensure a make-level descriptor + active version exist.
IF NOT EXISTS (SELECT 1 FROM SentinelDeviceDescriptor WHERE DeviceType = 'samsung')
    INSERT INTO SentinelDeviceDescriptor (DeviceType, DisplayName, Category, IsActive, CreateDate)
    VALUES ('samsung', 'Samsung', 'display', 1, @now);

DECLARE @smDescId int = (SELECT DescriptorID FROM SentinelDeviceDescriptor WHERE DeviceType = 'samsung');

IF NOT EXISTS (SELECT 1 FROM SentinelDeviceDescriptorVersion WHERE DescriptorId = @smDescId)
    INSERT INTO SentinelDeviceDescriptorVersion (DescriptorId, Version, DescriptorJson, IsActive, CreateDate, CreatedBy)
    VALUES (@smDescId, '1.0', @smJson, 1, @now, 'seed');
ELSE
    UPDATE SentinelDeviceDescriptorVersion SET DescriptorJson = @smJson
    WHERE DescriptorId = @smDescId AND IsActive = 1;

DECLARE @smVerId int = (SELECT TOP 1 VersionId FROM SentinelDeviceDescriptorVersion
                        WHERE DescriptorId = @smDescId AND IsActive = 1 ORDER BY VersionId DESC);
PRINT 'Samsung descriptor id=' + CAST(@smDescId AS varchar) + ' version=' + CAST(@smVerId AS varchar);

-- 3) Assign Samsung players that have no descriptor version yet (the evaluator skips unassigned devices).
UPDATE SentinelDeviceInstance
SET DescriptorID = @smDescId, DescriptorVersionId = @smVerId, LastEditDate = @now
WHERE CustomerID = 'dev' AND DescriptorVersionId IS NULL AND DisplayName LIKE 'Samsung%';
PRINT 'Samsung players assigned: ' + CAST(@@ROWCOUNT AS varchar);

COMMIT;
PRINT 'Done. LG/Samsung Engage content descriptors seeded; Samsung players assigned.';
