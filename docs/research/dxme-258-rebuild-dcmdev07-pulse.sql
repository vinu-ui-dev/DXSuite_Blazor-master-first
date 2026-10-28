/*
    DXME-258: bring DCMDEV07 [Pulse] onto SQL02's live Pulse schema for Sentinel Pulse reporting tests.

    Run on DCMDEV07 only. Pre-rebuild backup:
        D:\SQLBackups\Pulse_pre-DXME-258_2026-09-17.bak
    Roll back with:
        RESTORE DATABASE [Pulse] FROM DISK = N'D:\SQLBackups\Pulse_pre-DXME-258_2026-09-17.bak' WITH REPLACE

    Scope agreed on DXME-258:
      - Tables, columns, keys, indexes, FKs and default names match SQL02 (surveyed 2026-09-17).
      - SQL02 default bodies are not readable; the three new defaults assume getdate(), like every other CreateDate default.
      - View and procedure bodies stay DCMDEV07's. No SQL Agent jobs are created.
      - Lookup rows that differ are brought to SQL02's values. Existing DCMDEV07 devices, readings and customers are left in place.
      - One empty test customer, SNT, is added. No credential columns are copied from anywhere.

    Part 1 is one transaction: any error rolls everything back.
    Part 2 refreshes view metadata and reports any view that no longer compiles.
    Part 3 is read-only verification.
*/

USE [Pulse];
GO

IF CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) <> N'DCMDEV07'
    THROW 50000, 'This script only runs on DCMDEV07.', 1;
GO

/* ---------------------------------------------------------------- Part 1 */
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* 1. Objects SQL02 does not have */
    DROP TABLE dbo.tblActionAssignments;

    ALTER TABLE dbo.tblAlert  DROP CONSTRAINT FK_tblAlert_tblACustomer;
    ALTER TABLE dbo.tblAlert  DROP CONSTRAINT FK_tblAlert_tblADevice;
    ALTER TABLE dbo.tblDevice DROP CONSTRAINT FK_tblDevice_tblMonitorTemplate;
    ALTER TABLE dbo.tblDevice DROP CONSTRAINT FK_tblDevice_tblSchedule;

    /* 2. Table name case. Two steps, because a case-only sp_rename is not reliable. */
    EXEC sp_rename N'dbo.tblMonitorItem', N'tblMonitorItem_DXME258';
    EXEC sp_rename N'dbo.tblMonitorItem_DXME258', N'tblMonitoritem';

    /* 3. Column shapes */
    ALTER TABLE dbo.tblAction ALTER COLUMN Command varchar(100) NULL;
    ALTER TABLE dbo.tblAction DROP COLUMN IsScript, ScriptType;

    ALTER TABLE dbo.tblActionLog ADD Status int NULL, Message varchar(max) NULL;
    ALTER TABLE dbo.tblActionLog DROP COLUMN ActionStatusID, TriggerMessage, ExecutionMessage, LastEditDate, LastEditUser;

    ALTER TABLE dbo.tblAlert DROP COLUMN CurrentAlertState, AlertNotificationSentTime, ResolveNotificationSentTime, AlertResolveTime;

    ALTER TABLE dbo.tblApiKey DROP CONSTRAINT DF_tblApiKey_Active;
    ALTER TABLE dbo.tblApiKey DROP COLUMN BaseUrl, Platform, Active, LastSync;
    ALTER TABLE dbo.tblApiKey ALTER COLUMN ApiKey varchar(50) NOT NULL;

    ALTER TABLE dbo.tblArchiveHistory ADD FirstRecordDate datetime NULL, LastrecordDate datetime NULL;

    ALTER TABLE dbo.tblCustomer DROP CONSTRAINT DF_tblCustomer_IsClient, DF_tblCustomer_IsDSCustomer, DF_tblCustomer_Modified;
    ALTER TABLE dbo.tblCustomer DROP COLUMN Address1, City, State, Zipcode, IsClient, IsDSCustomer, PrimaryContact,
                                            Industry, SalesRep, Status, LogoPath, Modified;
    ALTER TABLE dbo.tblCustomer ADD MoxieLicenseExpireDate date NULL;

    ALTER TABLE dbo.tblDevice ADD LastAssignedContent varchar(max) NULL, LastAssignedDate datetime NULL;

    ALTER TABLE dbo.tblDisplay ADD DisplayManufacturer varchar(50) NULL, DisplayModel varchar(50) NULL;

    ALTER TABLE dbo.tblMonitorTemplateItem DROP CONSTRAINT DF_tblMonitorTemplateItem_CreateDate;
    ALTER TABLE dbo.tblMonitorTemplateItem DROP COLUMN CompareTypeID, AlertFrequency, Threshold, ErrorValue,
                                                       ErrorDescription, CreateDate, LastEditUser, LastEditDate;
    ALTER TABLE dbo.tblMonitorTemplateItem ADD Frequency int NULL;
    -- Dynamic SQL because the batch is compiled before Frequency exists.
    EXEC (N'UPDATE dbo.tblMonitorTemplateItem
            SET Frequency = CASE MonitorItemID WHEN 1 THEN 3 WHEN 2 THEN 3 WHEN 3 THEN 12 WHEN 9 THEN 400 ELSE 1 END
            WHERE MonitorTemplateID = 1;');

    ALTER TABLE dbo.tblParameter ADD CreateDate datetime NULL CONSTRAINT DF_tblParameter_CreateDate DEFAULT (getdate());

    /* 4. Tables only on SQL02 */
    CREATE TABLE dbo.tblSiteType (
        SiteTypeID  varchar(2)  NOT NULL CONSTRAINT PK_tblSiteType PRIMARY KEY CLUSTERED,
        Description varchar(50) NOT NULL,
        CreateDate  datetime    NULL CONSTRAINT DF_tblSiteType_CreateDate DEFAULT (getdate())
    );

    CREATE TABLE dbo.tblWebSite (
        RecordID      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblWebSite PRIMARY KEY CLUSTERED,
        SiteName      varchar(50)  NULL,
        HostingServer varchar(50)  NULL,
        URL           varchar(200) NULL,
        SiteTypeID    varchar(2)   NULL,
        SQLServer     varchar(50)  NULL,
        SQLDatabase   varchar(50)  NULL,
        CreateDate    datetime     NULL CONSTRAINT DF_tblWebSite_CreateDate DEFAULT (getdate())
    );
    CREATE UNIQUE NONCLUSTERED INDEX IX_tblWebSite ON dbo.tblWebSite (SiteName);

    /* 5. Indexes only on SQL02 */
    CREATE NONCLUSTERED INDEX IX_tblAlert ON dbo.tblAlert (MonitorItemID, ErrorLevel, ActiveEvent, LastEditDate)
        INCLUDE (DeviceID, ValueVarchar);
    CREATE NONCLUSTERED INDEX IX_tblAlert_CustomerID ON dbo.tblAlert (CustomerID)
        INCLUDE (DeviceID, AlertHeartbeat, MonitorItemID, ValueVarchar, ErrorLevel, ActiveEvent, LastEditDate);
    CREATE NONCLUSTERED INDEX IX_tblAlert_LastEditDate ON dbo.tblAlert (LastEditDate)
        INCLUDE (DeviceID, AlertHeartbeat, MonitorItemID, ValueVarchar, ErrorLevel);
    CREATE NONCLUSTERED INDEX IX_tblAlert_MessageID ON dbo.tblAlert (MessageID);
    CREATE NONCLUSTERED INDEX IX_tblAlert_MoniotorItem_ErrorLevel ON dbo.tblAlert (MonitorItemID, ErrorLevel, ActiveEvent, LastEditDate)
        INCLUDE (DeviceID, AlertHeartbeat, ValueVarchar);

    CREATE NONCLUSTERED INDEX IX_tblDevice_DeviceTypeID ON dbo.tblDevice (DeviceTypeID) INCLUDE (Monitor);
    CREATE NONCLUSTERED INDEX IX_tblDevice_Monitor_Snooze ON dbo.tblDevice (Monitor, Snooze)
        INCLUDE (Description, Heartbeat, ConnectedServer, Manufacturer, Model, SerialNumber, IPAddress, OSVersion, ContactEmailAddress);

    CREATE NONCLUSTERED INDEX IX_tblDeviceContent_DeviceID ON dbo.tblDeviceContent (DeviceID)
        INCLUDE (ConnectedServer, Description, AssignedContent);
    CREATE NONCLUSTERED INDEX IX_tblDeviceContent_DeviceID1 ON dbo.tblDeviceContent (DeviceID)
        INCLUDE (ConnectedServer, Description, Connected, AssignedContent, Metadata);

    CREATE NONCLUSTERED INDEX IX_CustValueErrorLevel ON dbo.tblPulse (MonitorItemID) INCLUDE (CustomerID, ValueInteger, ErrorLevel);
    CREATE NONCLUSTERED INDEX IX_DeviceMonitorItem ON dbo.tblPulse (DeviceID, MonitorItemID);
    CREATE NONCLUSTERED INDEX IX_MonitorItem ON dbo.tblPulse (MonitorItemID);
    CREATE NONCLUSTERED INDEX IX_MonitorItemHeartBeat ON dbo.tblPulse (MonitorItemID, Heartbeat) INCLUDE (ValueVarchar);
    CREATE NONCLUSTERED INDEX IX_tblPulse_MonitorItemID ON dbo.tblPulse (MonitorItemID, CreateDate);

    CREATE NONCLUSTERED INDEX IX_ScriptNameErroLeveLogTime ON dbo.tblScriptLog (ScriptName, ErrorLevel, LogTime);
    CREATE NONCLUSTERED INDEX IX_tblScriptLog_DeviceID_LogTime ON dbo.tblScriptLog (DeviceID, LogTime, ErrorLevel);
    CREATE NONCLUSTERED INDEX IX_tblScriptLog_Errorlevel ON dbo.tblScriptLog (ErrorLevel) INCLUDE (DeviceID, LogTime);

    /* 6. Lookup rows brought to SQL02's values */
    IF NOT EXISTS (SELECT 1 FROM dbo.tblDeviceType WHERE DeviceTypeID = 14)
        INSERT dbo.tblDeviceType (DeviceTypeID, Description) VALUES (14, 'QSC');

    INSERT dbo.tblSiteType (SiteTypeID, Description) VALUES
        ('1', 'Temp'), ('2', 'Temp 2'), ('EX', 'External Only'), ('IE', 'Internal/External'), ('IN', 'Internal Only');

    UPDATE mi SET mi.Description = s.Description, mi.MonitorTypeID = s.MonitorTypeID,
                  mi.DeviceTypeID = s.DeviceTypeID, mi.ItemTypeID = s.ItemTypeID
    FROM dbo.tblMonitoritem mi
    JOIN (VALUES
        (10, 'ScreenShot',           2,  0, 1),
        (70, 'Camera Status',        3,  9, 2),
        (71, 'TouchPanel Status',    3,  9, 2),
        (72, 'Communication Device', 3,  9, 2),
        (73, 'Bandwidth',            3,  9, 1),
        (74, 'QSCSystem',            3, 11, 1),
        (75, 'QSCCoreEventList',     3, 11, 1),
        (76, 'OmnivexInkDisplay',    3, 13, 2)
    ) s (MonitorItemID, Description, MonitorTypeID, DeviceTypeID, ItemTypeID) ON s.MonitorItemID = mi.MonitorItemID;

    DECLARE @Templates TABLE (MonitorTemplateID int PRIMARY KEY, TemplateDescription varchar(50), CustomerID varchar(20),
                              ItemList varchar(300), DeviceTypeID int NULL, CreateDate datetime NULL);
    INSERT @Templates VALUES
        (1,  'Default Player Template', 'RAD', '1,3,1,2,3,1,3,12,1,4,1,1,9,400,2,10,1,2,26,1,3,28,1,3,31,1,3,32,1,1,33,1,1,34,1,1,35,1,1,36,1,1,64,1,3,67,1,3', 1, NULL),
        (2,  'Player Modified Template Simple', 'RAD', '1,2,3,4,5,6', 0, NULL),
        (3,  'All Items Template', 'RAD', '1,1,3,2,1,3,3,1,3,4,1,1,8,1,4,9,1,4,10,1,2', 0, NULL),
        (15, 'All Items Testing', 'RAD', '1,1,3,2,1,3,3,1,3,4,1,1,10,1,2,8,1,4,9,1,4,26,1,3,27,1,3,28,1,1', 0, '2018-09-20T13:16:30.083'),
        (16, 'DLLTest Template', 'RAD', '1,1,3,2,1,3,3,1,3,4,1,1,8,1,4,9,1,4,10,1,2,26,1,3,28,1,1', 1, '2018-10-16T11:47:26.400'),
        (17, 'DLLTest Service', 'RAD', '1,1,3,2,1,3,3,1,3,4,1,1,8,1,4,9,1,4,28,1,1,29,1,3,31,1,3,32,1,3,33,1,1,34,1,1,35,1,3,36,1,1,37,1,1', 4, '2018-10-22T14:24:21.870'),
        (18, 'Default CMH', 'CMH', '1,1,1,2,1,1,3,1,1,4,1,1,8,400,2,9,400,2,10,1,2', 0, '2018-11-02T16:36:59.650'),
        (19, 'Moxie Player', 'CMH', '1,3,1,2,3,1,3,3,1,4,12,1,8,400,2,9,400,2,10,1,2,26,400,3,28,5,3,31,800,3,32,1200,1,33,1200,1,34,1200,1,64,200,3', 1, '2018-11-02T16:37:32.090'),
        (20, 'Moxie Server', 'CMH', '1,3,1,2,3,1,3,200,1,4,200,1,8,1200,2,9,1200,2,27,1,3', 2, '2018-11-02T17:39:12.297'),
        (21, 'Default Template', 'CAR', '1,3,3,2,3,3,3,3,3,4,1,1', 0, '2018-11-05T16:35:18.057'),
        (22, 'Signage Player', 'CAR', '1,3,3,2,3,3,3,3,3,4,1,1,8,200,4,9,200,4,10,1,2,26,1,3,28,12,1,31,200,3,32,1200,3,33,1200,1,34,1200,1,35,1200,3,36,1200,1', 1, '2018-11-05T16:36:35.550'),
        (23, 'Server Template', 'HAR', '1,3,3,2,3,3,3,3,3,4,1,1,8,200,4,9,200,4,27,1,3,31,200,3,32,1200,3,33,1200,1,34,1200,1', 2, '2018-11-06T09:16:07.613'),
        (24, 'Moxie Player', 'HAR', '1,3,3,2,3,3,3,3,3,4,1,1,8,200,4,9,200,4,10,1,2,26,1,3,28,12,1,31,200,3,32,1200,3,33,1200,1', 1, '2018-11-06T09:18:58.530'),
        (25, 'Default Player', 'NID', '1,3,3,2,3,3,3,100,3,4,25,1,9,200,4,10,3,2,26,1,3,28,25,1,31,400,3,32,1200,3,33,1200,1,34,1200,1', 1, '2019-01-21T09:55:57.980'),
        (26, 'New Server Template', 'RAD', '1,1,3,2,1,3,3,1,3,4,1,1,27,1,3,31,1,3,32,1,3,33,1,1,34,1,1,35,1,3,36,1,1', 2, '2019-02-07T13:36:35.870'),
        (27, 'Signage Server', 'CAR', '1,3,3,2,3,3,3,3,3,4,3,1,27,1,3,31,400,3,32,800,3,33,800,1,34,800,1,35,800,3,36,800,1', 2, '2019-02-26T11:13:12.790'),
        (28, 'Clickshare Template', 'RAD', '47,1,3,49,1,3,50,1,3', 5, '2019-05-09T17:26:21.983'),
        (29, 'BARCO SERVER', 'RAD', '1,1,3,2,1,3,3,1,3,4,1,1,48,1,3', 4, '2019-05-10T10:05:13.407'),
        (30, 'BCIC players', 'CMH', '1,1,1,2,1,1,3,1,1,4,1,1,8,1,2,31,1,3,32,1,1,33,1,1,34,1,1,35,1,1,36,1,1', 0, '2019-05-20T14:54:04.810'),
        (32, 'Mitch', 'RAD', '1,5,3,2,5,3,3,5,3,4,12,1,8,200,4,28,50,1,31,200,3,32,800,3,33,800,1,34,800,1,35,800,3,36,800,1', 4, '2020-01-02T10:34:29.497'),
        (33, 'Zoom Room', 'RAD', NULL, NULL, '2020-02-21T10:16:45.130'),
        (34, 'Matts Test', 'RAD', '4,1200,1,8,1,2', 1, '2020-03-23T09:13:43.220'),
        (35, 'Player Simple', 'NID', '1,5,3,2,5,3,3,5,3,4,12,1,8,400,4,9,400,4,10,5,2,26,5,3', 1, '2020-06-25T14:47:44.093'),
        (36, 'Reboot Logging Player', 'RAD', '1,1,1,2,1,1,3,200,1,4,12,1,9,200,2,10,1,2,31,1200,3,32,1200,1,33,1200,1,34,1200,1,35,1200,1,36,1200,1,38,12,1,51,3,1,64,1,3', 0, '2020-10-16T14:53:25.207'),
        (37, 'SCREENSHOT TEST', 'CMH', '2,1,1,10,1,1', 1, '2022-01-05T11:40:57.213'),
        (38, 'Ent Default', 'RAD', '1,1,1,2,1,1,3,1,1,4,12,1,8,400,2,26,1,3,51,1,1,64,1,3', 1, '2023-04-05T12:22:07.817'),
        (39, 'OSU Test', 'RAD', '4,400,1', 0, '2024-09-20T09:55:11.547'),
        (40, 'Avis Minimal', 'AVI', '3,200,1,4,12,1', 0, '2024-10-14T14:16:39.897'),
        (41, 'Player Monitor Template', 'RAD', '1,3,1,2,3,1,3,12,1,4,1,1,9,400,2,10,1,2,26,1,3,28,1,3,31,1,3,32,1,1,33,1,1,34,1,1,35,1,1,36,1,1,38,20,1,51,1,1,64,1,3,67,1,3', 1, '2024-11-25T14:19:05.513'),
        (42, 'Testing Where screenshots are in SQL', 'RAD', '1,1,1,2,100,1,10,200,2', 0, '2025-09-11T09:33:05.007'),
        (43, 'OSU Minimum', 'RAD', '4,1,1', 0, '2026-09-16T17:15:06.520');

    UPDATE mt SET mt.TemplateDescription = t.TemplateDescription, mt.CustomerID = t.CustomerID,
                  mt.ItemList = t.ItemList, mt.DeviceTypeID = t.DeviceTypeID
    FROM dbo.tblMonitorTemplate mt
    JOIN @Templates t ON t.MonitorTemplateID = mt.MonitorTemplateID;

    SET IDENTITY_INSERT dbo.tblMonitorTemplate ON;
    INSERT dbo.tblMonitorTemplate (MonitorTemplateID, TemplateDescription, CustomerID, ItemList, DeviceTypeID, Snooze, SnoozeTime, CreateDate)
    SELECT t.MonitorTemplateID, t.TemplateDescription, t.CustomerID, t.ItemList, t.DeviceTypeID, 0, 0, t.CreateDate
    FROM @Templates t
    WHERE NOT EXISTS (SELECT 1 FROM dbo.tblMonitorTemplate mt WHERE mt.MonitorTemplateID = t.MonitorTemplateID);
    SET IDENTITY_INSERT dbo.tblMonitorTemplate OFF;

    /* 7. Test customer for Sentinel. Cloud-hosted (OnPremise = 0) so the device-down procedure also closes its alerts. */
    INSERT dbo.tblCustomer (CustomerID, CustomerName, DLLVersion, OnPremise, Active, Resync, OffsiteBackup)
    VALUES ('SNT', 'Sentinel Pulse reporting test', '1.0.0.6', 0, 1, 0, 0);

    INSERT dbo.tblParameter (CustomerID, KeyName, Value, Comment)
    VALUES ('SNT', 'PruneDays', '15', 'DXME-258: matches RAD on SQL02 for retention tests');

    COMMIT TRANSACTION;
    PRINT 'Part 1 committed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'Part 1 rolled back. Nothing was changed.';
    THROW;
END CATCH;
GO

/* ---------------------------------------------------------------- Part 2 */
SET NOCOUNT ON;
DECLARE @View sysname, @Broken nvarchar(max) = N'';
DECLARE views CURSOR LOCAL FAST_FORWARD FOR
    SELECT QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) FROM sys.views ORDER BY name;
OPEN views;
FETCH NEXT FROM views INTO @View;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC sp_refreshview @View;
    END TRY
    BEGIN CATCH
        SET @Broken += @View + N': ' + ERROR_MESSAGE() + NCHAR(10);
    END CATCH;
    FETCH NEXT FROM views INTO @View;
END;
CLOSE views;
DEALLOCATE views;
PRINT CASE WHEN @Broken = N'' THEN N'Part 2: all views refreshed.' ELSE N'Part 2: views that failed to refresh:' + NCHAR(10) + @Broken END;
GO

/* ---------------------------------------------------------------- Part 3 */
SELECT 'tables' AS [check], COUNT(*) AS actual, 75 AS expected FROM sys.tables
UNION ALL SELECT 'named indexes', COUNT(*), 101 FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE i.name IS NOT NULL
UNION ALL SELECT 'foreign key constraints', COUNT(*), 66 FROM sys.foreign_keys
UNION ALL SELECT 'tblMonitoritem name case', COUNT(*), 1 FROM sys.tables WHERE name COLLATE Latin1_General_CS_AS = 'tblMonitoritem'
UNION ALL SELECT 'tblDevice columns', COUNT(*), 45 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblDevice')
UNION ALL SELECT 'tblCustomer columns', COUNT(*), 33 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblCustomer')
UNION ALL SELECT 'monitor items 70-76 with ItemTypeID', COUNT(*), 7 FROM dbo.tblMonitoritem WHERE MonitorItemID BETWEEN 70 AND 76 AND ItemTypeID IS NOT NULL
UNION ALL SELECT 'templates', COUNT(*), 31 FROM dbo.tblMonitorTemplate
UNION ALL SELECT 'SNT customer', COUNT(*), 1 FROM dbo.tblCustomer WHERE CustomerID = 'SNT';
GO
