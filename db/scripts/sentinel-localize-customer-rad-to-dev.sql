-- =============================================================================
-- LOCAL data fix (NOT an EF migration — this is data, not schema).
--
-- Consolidate the local Sentinel environment onto the 'dev' customer. Legacy rows
-- were created under 'RAD' (the old global CustomerInfo:CustomerCode); the local
-- tenant + agent now use 'dev'. This re-stamps every Sentinel customer-scoped table
-- 'RAD' -> 'dev' so the (dev-scoped) UI shows them.
--
-- Pair with: appsettings.Development.json CustomerInfo:CustomerCode = "dev"
-- (already set) so the Sentinel config tabs scope to 'dev' locally. Production
-- appsettings.json keeps "RAD" — do NOT run this against a production DB.
--
-- Idempotent: re-running is a no-op once no 'RAD' rows remain.
-- =============================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- Cloud connectors: their CustomerID determines the customer of every device they sync,
-- so this is what makes re-synced Ink/LG displays land under 'dev' after a device reset.
UPDATE SentinelCloudPlatformConnector    SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'CloudPlatformConnector: '    + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelDeviceInstance            SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'DeviceInstance: '            + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelDeviceSourceState         SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'DeviceSourceState: '         + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelDeviceCorrelationSuggestion SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'CorrelationSuggestion: '   + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelSiteGateway               SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'SiteGateway: '               + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelTask                      SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'Task: '                      + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelEnrollmentToken           SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'EnrollmentToken: '           + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelProbeProfile              SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'ProbeProfile: '              + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelProbeHistory              SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'ProbeHistory: '              + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelProbeHistorySummary       SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'ProbeHistorySummary: '       + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelRemediationPolicy         SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'RemediationPolicy: '         + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelItsmConnection            SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'ItsmConnection: '            + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelOutageCorrelation         SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'OutageCorrelation: '         + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelPatternAnalysis           SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'PatternAnalysis: '           + CAST(@@ROWCOUNT AS varchar);
UPDATE SentinelTicketTracking            SET CustomerID = 'dev' WHERE CustomerID = 'RAD'; PRINT 'TicketTracking: '            + CAST(@@ROWCOUNT AS varchar);

COMMIT;
PRINT 'Done. Sentinel local data consolidated onto customer ''dev''.';
