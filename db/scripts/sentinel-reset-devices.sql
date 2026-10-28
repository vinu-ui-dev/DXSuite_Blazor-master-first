-- =============================================================================
-- LOCAL device-data reset (data, not schema). Dev environment only.
--
-- Clears Sentinel's device records + device-linked history so the fleet repopulates
-- cleanly from the live sources after the SyncDirectorDevicesAsync Ink-folder fix:
--   * cloud connectors (Ink, LG, …)  -> their displays
--   * Engage players (Director device table, excluding inkf-* folders)
--   * the self-monitoring agent (host-gw*)
-- With MAC correlation, a player seen by both Engage and a connector merges into one
-- device with multiple source angles.
--
-- KEEPS configuration: descriptors + versions, site gateways, tasks/automations,
-- guardrail policies, ITSM, enrollment tokens. Only device + device-history rows go.
-- Do NOT run against production.
-- =============================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- Children that FK / reference DeviceInstance — delete before the parent.
DELETE FROM SentinelDeviceSourceState;          PRINT 'DeviceSourceState cleared: '       + CAST(@@ROWCOUNT AS varchar);
DELETE FROM SentinelPlatformDeviceLink;         PRINT 'PlatformDeviceLink cleared: '      + CAST(@@ROWCOUNT AS varchar);
DELETE FROM SentinelSiteGatewayDeviceMapping;   PRINT 'SiteGatewayDeviceMapping cleared: '+ CAST(@@ROWCOUNT AS varchar);
DELETE FROM SentinelFloorplanDevice;            PRINT 'FloorplanDevice cleared: '         + CAST(@@ROWCOUNT AS varchar);
DELETE FROM SentinelRemediationLog;             PRINT 'RemediationLog cleared: '          + CAST(@@ROWCOUNT AS varchar);
DELETE FROM SentinelProbeHistory;               PRINT 'ProbeHistory cleared: '            + CAST(@@ROWCOUNT AS varchar);
DELETE FROM SentinelProbeHistorySummary;        PRINT 'ProbeHistorySummary cleared: '     + CAST(@@ROWCOUNT AS varchar);
DELETE FROM SentinelDeviceCorrelationSuggestion;PRINT 'CorrelationSuggestion cleared: '   + CAST(@@ROWCOUNT AS varchar);

DELETE FROM SentinelDeviceInstance;             PRINT 'DeviceInstance cleared: '          + CAST(@@ROWCOUNT AS varchar);

COMMIT;
PRINT 'Done. Sentinel device data reset; sources will repopulate on the next sync/heartbeat cycle.';
