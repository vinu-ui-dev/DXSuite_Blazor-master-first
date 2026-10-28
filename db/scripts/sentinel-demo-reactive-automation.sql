-- =============================================================================
-- DEMO seed (data, not schema): close the reactive loop for agent hosts.
--
--   Trigger : a sentinel-agent-host device whose memory-used-percent > 85
--             (i.e. the host has gone Warning per the descriptor thresholds)
--   Action  : run Task "Host snapshot" (TaskID 2) on that host's own agent,
--             then resolve.
--
-- Scoped to the sentinel-agent-host descriptor (DescriptorID 5) so it only
-- applies to agent hosts. Re-runnable (guards on the definition name).
--
-- Remove later with:  UPDATE SentinelAutomationDefinition SET Status='draft'
--                     WHERE Name = 'Agent host: high memory capture';
-- =============================================================================

IF NOT EXISTS (SELECT 1 FROM SentinelAutomationDefinition WHERE Name = 'Agent host: high memory capture')
BEGIN
    INSERT INTO SentinelAutomationDefinition
        (Name, Description, AuthorType, DeviceDescriptorID, Category, TriggerJson,
         CurrentVersion, Status, Priority, CreatedBy, CreateDate)
    VALUES
        ('Agent host: high memory capture',
         'When an agent host''s memory exceeds 85%, run the Host snapshot task on it.',
         'engineer', 5, 'host-health',
         '{"conditions":[{"monitorPoint":"memory-used-percent","operator":"gt","value":"85"}],"matchMode":"any"}',
         '1.0', 'published', 100, 'system', GETUTCDATE());

    DECLARE @aid int = SCOPE_IDENTITY();

    INSERT INTO SentinelAutomationVersion
        (AutomationID, Version, ContentJson, Status, IsActive, PublishedAt, CreateDate, CreatedBy)
    VALUES
        (@aid, '1.0',
         '{"steps":[{"type":"run-script","taskId":2},{"type":"resolve","summary":"Captured host snapshot after high-memory alert"}]}',
         'published', 1, GETUTCDATE(), GETUTCDATE(), 'system');

    PRINT 'Created reactive automation (AutomationID=' + CAST(@aid AS varchar) + ') -> run-script TaskID 2';
END
ELSE
    PRINT 'Reactive automation already exists; nothing inserted.';
GO
