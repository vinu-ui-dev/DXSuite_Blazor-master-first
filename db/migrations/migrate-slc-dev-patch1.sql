-- ============================================================
-- DXSuite Migration Patch 1: SLC_DirectorDEV
-- Fixes three issues from the initial migration run:
--   1. tblLocationType missing ComponentTypeName + ShowOverrideSection columns
--   2. tblApprovalChain cascade cycle (SET NULL → NO ACTION on AccessRule FK)
--   3. Re-creates approval tables that failed due to issue #2
-- All operations are idempotent.
-- ============================================================

USE SLC_DirectorDEV;
GO

-- ============================================================
-- 1. Add missing columns to tblLocationType
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('tblLocationType') AND name = 'ComponentTypeName')
BEGIN
    ALTER TABLE tblLocationType ADD ComponentTypeName varchar(255) NULL;
    PRINT 'Added tblLocationType.ComponentTypeName';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('tblLocationType') AND name = 'ShowOverrideSection')
BEGIN
    ALTER TABLE tblLocationType ADD ShowOverrideSection bit NOT NULL
        CONSTRAINT DF_tblLocationType_ShowOverrideSection DEFAULT 0;
    PRINT 'Added tblLocationType.ShowOverrideSection';
END
GO

-- ============================================================
-- 2. Namespace remap (safe to re-run — WHERE clause is specific)
-- ============================================================

UPDATE tblLocationType
SET ComponentTypeName = REPLACE(
    ComponentTypeName,
    'RIADirector.Components.LocationTypes',
    'DXSuite.Director.Components.LocationTypes')
WHERE ComponentTypeName LIKE 'RIADirector.%';

PRINT 'Namespace remap applied (0 rows = no legacy types present, which is fine)';

SELECT LocationTypeId, LocationTypeName, ComponentTypeName
FROM tblLocationType
WHERE ComponentTypeName IS NOT NULL
ORDER BY LocationTypeId;
GO

-- ============================================================
-- 3. tblApprovalChain — cascade cycle fix
--    The original script used ON DELETE SET NULL on the
--    LocationAccessRule FK, which creates a multiple cascade
--    path via tblLocation. Changed to ON DELETE NO ACTION.
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblApprovalChain')
BEGIN
    CREATE TABLE tblApprovalChain (
        ApprovalChainId               int              NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblApprovalChain PRIMARY KEY,
        ContentPackageId              uniqueidentifier NOT NULL DEFAULT NEWID(),
        LocationId                    int              NOT NULL
            CONSTRAINT FK_tblApprovalChain_Location
                REFERENCES tblLocation (LocationId) ON DELETE CASCADE,
        IsEnabled                     bit              NOT NULL DEFAULT 1,
        IsDefault                     bit              NOT NULL DEFAULT 1,
        LocationAccessRuleId          int              NULL
            CONSTRAINT FK_tblApprovalChain_AccessRule
                REFERENCES tblLocationAccessRule (LocationAccessRuleId) ON DELETE NO ACTION,
        AutoApproveMembers            bit              NOT NULL DEFAULT 0,
        EnableLlmAutoApproval         bit              NOT NULL DEFAULT 0,
        LlmAutoApprovalUpToStageOrder int              NOT NULL DEFAULT 0,
        DeclinedContentRetentionDays  int              NOT NULL DEFAULT 3,
        CreateDate                    datetime         NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate                  datetime         NULL,
        LastEditUser                  varchar(50)      NULL
    );
    CREATE INDEX IX_tblApprovalChain_LocationId ON tblApprovalChain (LocationId);
    CREATE UNIQUE INDEX UX_tblApprovalChain_ContentPackageId
        ON tblApprovalChain (ContentPackageId);
    PRINT 'Created tblApprovalChain';
END
GO

-- ============================================================
-- 4. Approval tables that failed due to tblApprovalChain
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblApprovalStage')
BEGIN
    CREATE TABLE tblApprovalStage (
        ApprovalStageId    int          NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblApprovalStage PRIMARY KEY,
        ApprovalChainId    int          NOT NULL
            CONSTRAINT FK_tblApprovalStage_Chain
                REFERENCES tblApprovalChain (ApprovalChainId) ON DELETE CASCADE,
        StageOrder         int          NOT NULL,
        StageName          varchar(100) NULL,
        IntraStageOperator varchar(3)   NOT NULL DEFAULT 'OR'
    );
    CREATE UNIQUE INDEX UX_tblApprovalStage_ChainOrder
        ON tblApprovalStage (ApprovalChainId, StageOrder);
    PRINT 'Created tblApprovalStage';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblApprovalStageGroup')
BEGIN
    CREATE TABLE tblApprovalStageGroup (
        ApprovalStageGroupId int NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblApprovalStageGroup PRIMARY KEY,
        ApprovalStageId      int NOT NULL
            CONSTRAINT FK_tblApprovalStageGroup_Stage
                REFERENCES tblApprovalStage (ApprovalStageId) ON DELETE CASCADE,
        UserGroupId          int NOT NULL
            CONSTRAINT FK_tblApprovalStageGroup_UserGroup
                REFERENCES tblUserGroup (UserGroupId) ON DELETE NO ACTION,
        SortOrder            int NOT NULL DEFAULT 0
    );
    CREATE UNIQUE INDEX UX_tblApprovalStageGroup_StageGroup
        ON tblApprovalStageGroup (ApprovalStageId, UserGroupId);
    PRINT 'Created tblApprovalStageGroup';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblApprovalRequest')
BEGIN
    CREATE TABLE tblApprovalRequest (
        ApprovalRequestId int         NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblApprovalRequest PRIMARY KEY,
        AssetApprovalId   int         NOT NULL
            CONSTRAINT FK_tblApprovalRequest_AssetApproval
                REFERENCES tblAssetApproval (AssetApprovalId) ON DELETE CASCADE,
        ApprovalChainId   int         NOT NULL
            CONSTRAINT FK_tblApprovalRequest_Chain
                REFERENCES tblApprovalChain (ApprovalChainId) ON DELETE NO ACTION,
        CurrentStageId    int         NULL
            CONSTRAINT FK_tblApprovalRequest_CurrentStage
                REFERENCES tblApprovalStage (ApprovalStageId) ON DELETE NO ACTION,
        Status            varchar(20) NOT NULL DEFAULT 'pending',
        SubmittedDate     datetime    NOT NULL DEFAULT GETUTCDATE(),
        CompletedDate     datetime    NULL
    );
    CREATE INDEX IX_tblApprovalRequest_AssetApprovalId ON tblApprovalRequest (AssetApprovalId);
    CREATE INDEX IX_tblApprovalRequest_Status ON tblApprovalRequest (Status);
    PRINT 'Created tblApprovalRequest';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblApprovalQueueEntry')
BEGIN
    CREATE TABLE tblApprovalQueueEntry (
        QueueEntryId      int         NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblApprovalQueueEntry PRIMARY KEY,
        ApprovalRequestId int         NOT NULL
            CONSTRAINT FK_tblApprovalQueueEntry_Request
                REFERENCES tblApprovalRequest (ApprovalRequestId) ON DELETE CASCADE,
        ApprovalStageId   int         NOT NULL
            CONSTRAINT FK_tblApprovalQueueEntry_Stage
                REFERENCES tblApprovalStage (ApprovalStageId) ON DELETE NO ACTION,
        UserGroupId       int         NOT NULL
            CONSTRAINT FK_tblApprovalQueueEntry_UserGroup
                REFERENCES tblUserGroup (UserGroupId) ON DELETE NO ACTION,
        Status            varchar(20) NOT NULL DEFAULT 'pending',
        CreatedDate       datetime    NOT NULL DEFAULT GETUTCDATE(),
        ResolvedDate      datetime    NULL,
        ResolvedByUserId  int         NULL
            CONSTRAINT FK_tblApprovalQueueEntry_ResolvedBy
                REFERENCES tblUser (UserId) ON DELETE NO ACTION,
        RowVersion        rowversion  NOT NULL
    );
    CREATE INDEX IX_tblApprovalQueueEntry_RequestStatus
        ON tblApprovalQueueEntry (ApprovalRequestId, Status);
    PRINT 'Created tblApprovalQueueEntry';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblApprovalAction')
BEGIN
    CREATE TABLE tblApprovalAction (
        ApprovalActionId  int          NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblApprovalAction PRIMARY KEY,
        ApprovalRequestId int          NOT NULL
            CONSTRAINT FK_tblApprovalAction_Request
                REFERENCES tblApprovalRequest (ApprovalRequestId) ON DELETE CASCADE,
        ApprovalStageId   int          NOT NULL
            CONSTRAINT FK_tblApprovalAction_Stage
                REFERENCES tblApprovalStage (ApprovalStageId) ON DELETE NO ACTION,
        UserGroupId       int          NOT NULL
            CONSTRAINT FK_tblApprovalAction_UserGroup
                REFERENCES tblUserGroup (UserGroupId) ON DELETE NO ACTION,
        ActingUserId      int          NOT NULL
            CONSTRAINT FK_tblApprovalAction_User
                REFERENCES tblUser (UserId) ON DELETE NO ACTION,
        ActionType        varchar(20)  NOT NULL,
        Notes             varchar(500) NULL,
        ActionDate        datetime     NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT 'Created tblApprovalAction';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblApprovalComment')
BEGIN
    CREATE TABLE tblApprovalComment (
        ApprovalCommentId int            NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblApprovalComment PRIMARY KEY,
        ApprovalRequestId int            NULL
            CONSTRAINT FK_tblApprovalComment_Request
                REFERENCES tblApprovalRequest (ApprovalRequestId) ON DELETE NO ACTION,
        AssetApprovalId   int            NOT NULL
            CONSTRAINT FK_tblApprovalComment_AssetApproval
                REFERENCES tblAssetApproval (AssetApprovalId) ON DELETE CASCADE,
        UserId            int            NOT NULL
            CONSTRAINT FK_tblApprovalComment_User
                REFERENCES tblUser (UserId) ON DELETE NO ACTION,
        CommentText       nvarchar(2000) NOT NULL,
        CommentType       varchar(20)    NOT NULL DEFAULT 'general',
        CreatedDate       datetime       NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE INDEX IX_tblApprovalComment_AssetApprovalId ON tblApprovalComment (AssetApprovalId);
    PRINT 'Created tblApprovalComment';
END
GO

-- ============================================================
-- 5. Location type seed data (now that columns exist)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeName = 'VideoWall')
BEGIN
    INSERT INTO tblLocationType (LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection, CreateDate)
    VALUES ('VideoWall', 1, 1, 'DXSuite.Director.Components.LocationTypes.Custom.VideoWall', 1, GETUTCDATE());
    PRINT 'Seeded tblLocationType: VideoWall';
END
GO

IF NOT EXISTS (SELECT 1 FROM tblLocationType WHERE LocationTypeName = 'AirlineClusterView')
BEGIN
    INSERT INTO tblLocationType (LocationTypeName, LinkedToDisplay, IsCustom, ComponentTypeName, ShowOverrideSection, CreateDate)
    VALUES ('AirlineClusterView', 1, 1, 'DXSuite.Director.Components.LocationTypes.Custom.AirlineClusterView', 0, GETUTCDATE());
    PRINT 'Seeded tblLocationType: AirlineClusterView';
END
GO

PRINT '';
PRINT '============================================================';
PRINT 'Patch 1 complete.';
PRINT '============================================================';
