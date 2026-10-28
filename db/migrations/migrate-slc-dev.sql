-- ============================================================
-- DXSuite Migration: SLC_DirectorDEV
-- Generated: 2026-05-30
-- Safe to re-run: all operations are idempotent (IF NOT EXISTS).
-- Does NOT drop or truncate any existing tables or data.
-- ============================================================

USE SLC_DirectorDEV;
GO

-- ============================================================
-- 1. Remap location type component namespaces
--    RIADirector.Components.LocationTypes → DXSuite.Director.Components.LocationTypes
-- ============================================================
PRINT '>> Remapping tblLocationType component names...';

UPDATE tblLocationType
SET ComponentTypeName = REPLACE(
    ComponentTypeName,
    'RIADirector.Components.LocationTypes',
    'DXSuite.Director.Components.LocationTypes')
WHERE ComponentTypeName LIKE 'RIADirector.%';

SELECT LocationTypeId, LocationTypeName, ComponentTypeName
FROM tblLocationType
WHERE ComponentTypeName IS NOT NULL
ORDER BY LocationTypeId;
GO

-- ============================================================
-- 2. User groups and membership
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblUserGroup')
BEGIN
    CREATE TABLE tblUserGroup (
        UserGroupId     int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblUserGroup PRIMARY KEY,
        GroupName       varchar(100)  NOT NULL,
        Description     varchar(500)  NULL,
        IsActive        bit           NOT NULL DEFAULT 1,
        GroupType       varchar(30)   NOT NULL DEFAULT 'director',
        ExternalId      varchar(255)  NULL,
        CreateDate      datetime      NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate    datetime      NULL,
        LastEditUser    varchar(50)   NULL
    );
    CREATE UNIQUE INDEX UX_tblUserGroup_GroupName ON tblUserGroup (GroupName);
    PRINT 'Created tblUserGroup';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblUserGroupMembership')
BEGIN
    CREATE TABLE tblUserGroupMembership (
        MembershipId    int      NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblUserGroupMembership PRIMARY KEY,
        UserGroupId     int      NOT NULL
            CONSTRAINT FK_tblUserGroupMembership_Group
                REFERENCES tblUserGroup (UserGroupId) ON DELETE CASCADE,
        UserId          int      NOT NULL
            CONSTRAINT FK_tblUserGroupMembership_User
                REFERENCES tblUser (UserId) ON DELETE CASCADE,
        AddedDate       datetime NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE UNIQUE INDEX UX_tblUserGroupMembership_GroupUser
        ON tblUserGroupMembership (UserGroupId, UserId);
    PRINT 'Created tblUserGroupMembership';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblUserMetadata')
BEGIN
    CREATE TABLE tblUserMetadata (
        UserMetadataId  int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblUserMetadata PRIMARY KEY,
        UserId          int           NOT NULL
            CONSTRAINT FK_tblUserMetadata_User
                REFERENCES tblUser (UserId) ON DELETE CASCADE,
        MetadataKey     varchar(50)   NOT NULL,
        MetadataValue   varchar(255)  NOT NULL,
        CreateDate      datetime      NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE UNIQUE INDEX UX_tblUserMetadata_UserKey
        ON tblUserMetadata (UserId, MetadataKey);
    PRINT 'Created tblUserMetadata';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblUserGroupMetadata')
BEGIN
    CREATE TABLE tblUserGroupMetadata (
        UserGroupMetadataId int          NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblUserGroupMetadata PRIMARY KEY,
        UserGroupId         int          NOT NULL
            CONSTRAINT FK_tblUserGroupMetadata_Group
                REFERENCES tblUserGroup (UserGroupId) ON DELETE CASCADE,
        MetadataKey         varchar(50)  NOT NULL,
        MetadataValue       varchar(255) NOT NULL,
        CreateDate          datetime     NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE UNIQUE INDEX UX_tblUserGroupMetadata_GroupKey
        ON tblUserGroupMetadata (UserGroupId, MetadataKey);
    PRINT 'Created tblUserGroupMetadata';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblDiscoveredAdGroup')
BEGIN
    CREATE TABLE tblDiscoveredAdGroup (
        DiscoveredAdGroupId int          NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblDiscoveredAdGroup PRIMARY KEY,
        ObjectId            varchar(36)  NOT NULL,
        DisplayName         varchar(200) NULL,
        FirstSeenDate       datetime     NOT NULL DEFAULT GETUTCDATE(),
        LastSeenDate        datetime     NOT NULL DEFAULT GETUTCDATE(),
        IsDismissed         bit          NOT NULL DEFAULT 0,
        IsAdopted           bit          NOT NULL DEFAULT 0,
        AdoptedUserGroupId  int          NULL
            CONSTRAINT FK_tblDiscoveredAdGroup_UserGroup
                REFERENCES tblUserGroup (UserGroupId) ON DELETE SET NULL
    );
    CREATE UNIQUE INDEX UX_tblDiscoveredAdGroup_ObjectId
        ON tblDiscoveredAdGroup (ObjectId);
    PRINT 'Created tblDiscoveredAdGroup';
END
GO

-- ============================================================
-- 3. Metadata definitions, notifications, local accounts, settings
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblMetadataDefinition')
BEGIN
    CREATE TABLE tblMetadataDefinition (
        MetadataDefinitionId int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblMetadataDefinition PRIMARY KEY,
        MetadataKey          varchar(50)   NOT NULL,
        DisplayName          varchar(100)  NOT NULL,
        FieldType            varchar(20)   NOT NULL DEFAULT 'string',
        OptionsJson          nvarchar(max) NULL,
        Description          varchar(255)  NULL,
        SortOrder            int           NOT NULL DEFAULT 0,
        CreateDate           datetime      NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate         datetime      NULL,
        LastEditUser         varchar(50)   NULL
    );
    PRINT 'Created tblMetadataDefinition';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblUserNotificationPreference')
BEGIN
    CREATE TABLE tblUserNotificationPreference (
        UserNotificationPreferenceId int         NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblUserNotificationPreference PRIMARY KEY,
        UserId                        int         NOT NULL
            CONSTRAINT FK_tblUserNotificationPreference_User
                REFERENCES tblUser (UserId) ON DELETE CASCADE,
        Category                      varchar(30) NOT NULL,
        EmailEnabled                  bit         NOT NULL DEFAULT 0,
        LastEditDate                  datetime    NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT 'Created tblUserNotificationPreference';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblLocalAccount')
BEGIN
    CREATE TABLE tblLocalAccount (
        LocalAccountId          int              NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblLocalAccount PRIMARY KEY,
        Email                   nvarchar(256)    NOT NULL,
        NormalizedEmail         nvarchar(256)    NOT NULL,
        PasswordHash            nvarchar(512)    NOT NULL,
        EmailConfirmed          bit              NOT NULL DEFAULT 0,
        LockoutEnd              datetimeoffset   NULL,
        AccessFailedCount       int              NOT NULL DEFAULT 0,
        UserId                  int              NOT NULL
            CONSTRAINT FK_tblLocalAccount_User
                REFERENCES tblUser (UserId) ON DELETE CASCADE,
        CreateDate              datetime         NOT NULL DEFAULT GETUTCDATE(),
        LastLoginDate           datetime         NULL,
        ConfirmationToken       nvarchar(128)    NULL,
        ConfirmationTokenExpiry datetime         NULL
    );
    CREATE UNIQUE INDEX UX_tblLocalAccount_NormalizedEmail
        ON tblLocalAccount (NormalizedEmail);
    PRINT 'Created tblLocalAccount';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblNotification')
BEGIN
    CREATE TABLE tblNotification (
        NotificationId   int            NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblNotification PRIMARY KEY,
        UserId           int            NOT NULL
            CONSTRAINT FK_tblNotification_User
                REFERENCES tblUser (UserId) ON DELETE CASCADE,
        Title            nvarchar(200)  NOT NULL,
        Message          nvarchar(500)  NULL,
        NotificationType varchar(30)    NOT NULL,
        ReferenceId      int            NULL,
        ReferenceUrl     varchar(200)   NULL,
        IsRead           bit            NOT NULL DEFAULT 0,
        EmailSent        bit            NOT NULL DEFAULT 0,
        CreatedDate      datetime       NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE INDEX IX_tblNotification_UserRead ON tblNotification (UserId, IsRead);
    PRINT 'Created tblNotification';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblSystemSetting')
BEGIN
    CREATE TABLE tblSystemSetting (
        SettingKey   varchar(100)   NOT NULL
            CONSTRAINT PK_tblSystemSetting PRIMARY KEY,
        SettingValue nvarchar(2000) NULL,
        LastEditDate datetime       NOT NULL DEFAULT GETUTCDATE(),
        LastEditUser varchar(50)    NULL
    );
    PRINT 'Created tblSystemSetting';
END
GO

-- ============================================================
-- 4. Location access rules and approval workflow
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblLocationAccessRule')
BEGIN
    CREATE TABLE tblLocationAccessRule (
        LocationAccessRuleId  int               NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblLocationAccessRule PRIMARY KEY,
        ContentPackageId      uniqueidentifier  NOT NULL DEFAULT NEWID(),
        LocationId            int               NOT NULL
            CONSTRAINT FK_tblLocationAccessRule_Location
                REFERENCES tblLocation (LocationId) ON DELETE CASCADE,
        UserId                int               NULL
            CONSTRAINT FK_tblLocationAccessRule_User
                REFERENCES tblUser (UserId) ON DELETE NO ACTION,
        UserGroupId           int               NULL
            CONSTRAINT FK_tblLocationAccessRule_UserGroup
                REFERENCES tblUserGroup (UserGroupId) ON DELETE NO ACTION,
        AccessLevel           varchar(20)       NOT NULL DEFAULT 'full',
        MetadataFilterKey     varchar(50)       NULL,
        CreateDate            datetime          NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate          datetime          NULL,
        LastEditUser          varchar(50)       NULL
    );
    CREATE UNIQUE INDEX UX_tblLocationAccessRule_Unique
        ON tblLocationAccessRule (LocationId, UserId, UserGroupId, AccessLevel);
    CREATE UNIQUE INDEX UX_tblLocationAccessRule_ContentPackageId
        ON tblLocationAccessRule (ContentPackageId);
    PRINT 'Created tblLocationAccessRule';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblAssetApproval')
BEGIN
    CREATE TABLE tblAssetApproval (
        AssetApprovalId     int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblAssetApproval PRIMARY KEY,
        AssetId             int           NOT NULL
            CONSTRAINT FK_tblAssetApproval_Asset
                REFERENCES tblAsset (AssetId) ON DELETE CASCADE,
        ApprovalStatus      int           NOT NULL DEFAULT 0,
        UploadedByUserId    int           NOT NULL
            CONSTRAINT FK_tblAssetApproval_UploadedBy
                REFERENCES tblUser (UserId) ON DELETE NO ACTION,
        ReviewedByUserId    int           NULL
            CONSTRAINT FK_tblAssetApproval_ReviewedBy
                REFERENCES tblUser (UserId) ON DELETE NO ACTION,
        SubmittedDate       datetime      NOT NULL DEFAULT GETUTCDATE(),
        ReviewedDate        datetime      NULL,
        ReviewNotes         varchar(500)  NULL,
        DeclinedDate        datetime      NULL,
        ExtendedData        nvarchar(max) NULL
    );
    CREATE INDEX IX_tblAssetApproval_ApprovalStatus ON tblAssetApproval (ApprovalStatus);
    PRINT 'Created tblAssetApproval';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblAssetContentMetadata')
BEGIN
    CREATE TABLE tblAssetContentMetadata (
        AssetContentMetadataId int              NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblAssetContentMetadata PRIMARY KEY,
        ContentPackageId       uniqueidentifier NOT NULL DEFAULT NEWID(),
        AssetId                int              NOT NULL
            CONSTRAINT FK_tblAssetContentMetadata_Asset
                REFERENCES tblAsset (AssetId) ON DELETE CASCADE,
        MetadataKey            nvarchar(50)     NOT NULL,
        MetadataValue          nvarchar(255)    NULL,
        SetByUserId            int              NULL
            CONSTRAINT FK_tblAssetContentMetadata_User
                REFERENCES tblUser (UserId) ON DELETE NO ACTION,
        CreateDate             datetime         NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE UNIQUE INDEX UX_tblAssetContentMetadata_ContentPackageId
        ON tblAssetContentMetadata (ContentPackageId);
    PRINT 'Created tblAssetContentMetadata';
END
GO

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
-- 5. Cloud platform connectors
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblCloudPlatformConnector')
BEGIN
    CREATE TABLE tblCloudPlatformConnector (
        ConnectorId         int          NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblCloudPlatformConnector PRIMARY KEY,
        PlatformId          varchar(50)  NOT NULL,
        DisplayName         varchar(100) NOT NULL,
        CustomerId          varchar(20)  NOT NULL,
        ConfigJson          varchar(max) NOT NULL,
        IsEnabled           bit          NOT NULL DEFAULT 1,
        GatewayMode         varchar(20)  NOT NULL DEFAULT 'none',
        ContentMode         varchar(30)  NOT NULL DEFAULT 'monitoring-only',
        EnabledImports      varchar(500) NULL,
        LastSyncAt          datetime     NULL,
        LastSyncStatus      varchar(50)  NULL,
        LastSyncMessage     varchar(500) NULL,
        CredentialExpiresAt datetime     NULL,
        CreateDate          datetime     NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate        datetime     NULL
    );
    PRINT 'Created tblCloudPlatformConnector';
END
GO

-- ============================================================
-- 6. LLM integration
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblLlmProviderConfig')
BEGIN
    CREATE TABLE tblLlmProviderConfig (
        ConfigId            int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblLlmProviderConfig PRIMARY KEY,
        ProviderId          nvarchar(50)  NOT NULL,
        ProviderType        nvarchar(30)  NOT NULL,
        DisplayName         nvarchar(100) NOT NULL,
        BaseUrl             nvarchar(500) NULL,
        ApiKey              nvarchar(500) NULL,
        Model               nvarchar(100) NOT NULL,
        MaxTokens           int           NOT NULL DEFAULT 2000,
        DefaultTemperature  real          NOT NULL DEFAULT 0.1,
        TimeoutSeconds      int           NOT NULL DEFAULT 30,
        IsDefault           bit           NOT NULL DEFAULT 0,
        IsEnabled           bit           NOT NULL DEFAULT 1,
        CredentialExpiresAt datetime      NULL,
        CreateDate          datetime      NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate        datetime      NULL
    );
    PRINT 'Created tblLlmProviderConfig';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblLlmPromptConfig')
BEGIN
    CREATE TABLE tblLlmPromptConfig (
        PromptId       int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblLlmPromptConfig PRIMARY KEY,
        UseCaseId      nvarchar(50)  NOT NULL,
        DisplayName    nvarchar(100) NOT NULL,
        ProviderId     nvarchar(50)  NULL,
        SystemPrompt   nvarchar(max) NOT NULL,
        Temperature    real          NOT NULL DEFAULT 0.1,
        MaxTokens      int           NOT NULL DEFAULT 1000,
        ResponseFormat nvarchar(20)  NULL,
        SupportsText   bit           NOT NULL DEFAULT 1,
        SupportsVision bit           NOT NULL DEFAULT 0,
        LastTestedDate datetime      NULL,
        IsEnabled      bit           NOT NULL DEFAULT 1,
        CreateDate     datetime      NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate   datetime      NULL,
        LastEditUser   nvarchar(100) NULL
    );
    PRINT 'Created tblLlmPromptConfig';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblLlmInteraction')
BEGIN
    CREATE TABLE tblLlmInteraction (
        InteractionId    bigint       NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblLlmInteraction PRIMARY KEY,
        Provider         varchar(50)  NOT NULL,
        Model            varchar(100) NOT NULL,
        PromptHash       varchar(64)  NOT NULL,
        PromptTokens     int          NOT NULL DEFAULT 0,
        CompletionTokens int          NOT NULL DEFAULT 0,
        RequestJson      varchar(max) NULL,
        ResponseJson     varchar(max) NULL,
        LatencyMs        bigint       NOT NULL DEFAULT 0,
        Success          bit          NOT NULL DEFAULT 1,
        ErrorMessage     varchar(500) NULL,
        Purpose          varchar(50)  NOT NULL,
        CreateDate       datetime     NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT 'Created tblLlmInteraction';
END
GO

-- ============================================================
-- 7. Engage (control client + device pairing)
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblControlClient')
BEGIN
    CREATE TABLE tblControlClient (
        ControlClientId  int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblControlClient PRIMARY KEY,
        ClientName       nvarchar(100) NOT NULL,
        AuthMode         varchar(20)   NOT NULL DEFAULT 'accesscode',
        AccessCodeHash   varchar(128)  NULL,
        AccessCodePrefix varchar(16)   NULL,
        LocationId       int           NOT NULL
            CONSTRAINT FK_tblControlClient_Location
                REFERENCES tblLocation (LocationId) ON DELETE CASCADE,
        IsActive         bit           NOT NULL DEFAULT 1,
        LastAuthAt       datetime      NULL,
        LastAuthIp       varchar(45)   NULL,
        CreatedBy        int           NULL
            CONSTRAINT FK_tblControlClient_CreatedBy
                REFERENCES tblUser (UserId) ON DELETE SET NULL,
        CreateDate       datetime      NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate     datetime      NULL
    );
    PRINT 'Created tblControlClient';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblControlClientDisplay')
BEGIN
    CREATE TABLE tblControlClientDisplay (
        ControlClientDisplayId int NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblControlClientDisplay PRIMARY KEY,
        ControlClientId        int NOT NULL
            CONSTRAINT FK_tblControlClientDisplay_Client
                REFERENCES tblControlClient (ControlClientId) ON DELETE CASCADE,
        DisplayId              int NOT NULL
            CONSTRAINT FK_tblControlClientDisplay_Display
                REFERENCES tblDisplay (DisplayId) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_tblControlClientDisplay_ClientDisplay
        ON tblControlClientDisplay (ControlClientId, DisplayId);
    PRINT 'Created tblControlClientDisplay';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblControlClientPairingRequest')
BEGIN
    CREATE TABLE tblControlClientPairingRequest (
        PairingRequestId  uniqueidentifier NOT NULL DEFAULT NEWID()
            CONSTRAINT PK_tblControlClientPairingRequest PRIMARY KEY,
        PairingCodeHash   varchar(128)     NOT NULL,
        Status            varchar(20)      NOT NULL DEFAULT 'pending',
        ControlClientId   int              NULL
            CONSTRAINT FK_tblControlClientPairingRequest_Client
                REFERENCES tblControlClient (ControlClientId) ON DELETE SET NULL,
        ClientIp          varchar(45)      NULL,
        ClientHostname    nvarchar(100)    NULL,
        ExpiresAt         datetime         NOT NULL,
        CreateDate        datetime         NOT NULL DEFAULT GETUTCDATE(),
        ResolvedAt        datetime         NULL,
        FailedAttempts    int              NOT NULL DEFAULT 0,
        ResolvedByUserId  int              NULL
            CONSTRAINT FK_tblControlClientPairingRequest_ResolvedBy
                REFERENCES tblUser (UserId) ON DELETE NO ACTION
    );
    CREATE INDEX IX_tblControlClientPairingRequest_PairingCodeHash
        ON tblControlClientPairingRequest (PairingCodeHash);
    CREATE INDEX IX_tblControlClientPairingRequest_Status
        ON tblControlClientPairingRequest (Status);
    PRINT 'Created tblControlClientPairingRequest';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblControlClientToken')
BEGIN
    CREATE TABLE tblControlClientToken (
        ControlClientTokenId int          NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblControlClientToken PRIMARY KEY,
        ControlClientId      int          NOT NULL
            CONSTRAINT FK_tblControlClientToken_Client
                REFERENCES tblControlClient (ControlClientId) ON DELETE CASCADE,
        TokenHash            varchar(128) NOT NULL,
        TokenPrefix          varchar(16)  NULL,
        IsActive             bit          NOT NULL DEFAULT 1,
        LastUsedAt           datetime     NULL,
        CreateDate           datetime     NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE UNIQUE INDEX UX_tblControlClientToken_TokenHash
        ON tblControlClientToken (TokenHash);
    PRINT 'Created tblControlClientToken';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblDevicePairingRequest')
BEGIN
    CREATE TABLE tblDevicePairingRequest (
        PairingRequestId  uniqueidentifier NOT NULL DEFAULT NEWID()
            CONSTRAINT PK_tblDevicePairingRequest PRIMARY KEY,
        PairingCodeHash   varchar(128)     NOT NULL,
        Status            varchar(20)      NOT NULL DEFAULT 'pending',
        ExpiresAt         datetime         NOT NULL,
        CreateDate        datetime         NOT NULL DEFAULT GETUTCDATE(),
        ResolvedAt        datetime         NULL,
        DeviceId          varchar(50)      NULL,
        Manufacturer      varchar(255)     NULL,
        Model             varchar(255)     NULL,
        SerialNumber      varchar(50)      NULL,
        IpAddress         varchar(50)      NULL,
        MacAddress        varchar(50)      NULL,
        FailedAttempts    int              NOT NULL DEFAULT 0,
        ResolvedByUserId  int              NULL
            CONSTRAINT FK_tblDevicePairingRequest_ResolvedBy
                REFERENCES tblUser (UserId) ON DELETE NO ACTION
    );
    CREATE INDEX IX_tblDevicePairingRequest_PairingCodeHash
        ON tblDevicePairingRequest (PairingCodeHash);
    CREATE INDEX IX_tblDevicePairingRequest_Status
        ON tblDevicePairingRequest (Status);
    PRINT 'Created tblDevicePairingRequest';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblDeviceToken')
BEGIN
    CREATE TABLE tblDeviceToken (
        DeviceTokenId    int              NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblDeviceToken PRIMARY KEY,
        DeviceId         varchar(50)      NULL,
        TokenHash        varchar(128)     NOT NULL,
        TokenPrefix      varchar(16)      NULL,
        IsActive         bit              NOT NULL DEFAULT 1,
        LastUsedAt       datetime         NULL,
        CreateDate       datetime         NOT NULL DEFAULT GETUTCDATE(),
        PairingRequestId uniqueidentifier NULL,
        ApprovedByUserId int              NULL
            CONSTRAINT FK_tblDeviceToken_ApprovedBy
                REFERENCES tblUser (UserId) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX UX_tblDeviceToken_TokenHash ON tblDeviceToken (TokenHash);
    PRINT 'Created tblDeviceToken';
END
GO

-- ============================================================
-- 8. Floor plan system
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblFloorPlanElement')
BEGIN
    CREATE TABLE tblFloorPlanElement (
        FloorPlanElementId int              NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblFloorPlanElement PRIMARY KEY,
        ContentPackageId   uniqueidentifier NOT NULL DEFAULT NEWID(),
        LocationId         int              NOT NULL
            CONSTRAINT FK_tblFloorPlanElement_Location
                REFERENCES tblLocation (LocationId) ON DELETE CASCADE,
        ChildLocationId    int              NULL
            CONSTRAINT FK_tblFloorPlanElement_ChildLocation
                REFERENCES tblLocation (LocationId) ON DELETE NO ACTION,
        ChildDisplayId     int              NULL
            CONSTRAINT FK_tblFloorPlanElement_ChildDisplay
                REFERENCES tblDisplay (DisplayId) ON DELETE NO ACTION,
        PositionTop        float            NOT NULL DEFAULT 0,
        PositionLeft       float            NOT NULL DEFAULT 0,
        ElementWidth       float            NOT NULL DEFAULT 100,
        ElementHeight      float            NOT NULL DEFAULT 100,
        IconPath           varchar(255)     NULL,
        IconLabel          varchar(50)      NULL,
        ShowLivePreview    bit              NOT NULL DEFAULT 0,
        Latitude           float            NULL,
        Longitude          float            NULL,
        Position3DX        float            NULL,
        Position3DY        float            NULL,
        Position3DZ        float            NULL,
        SortOrder          int              NOT NULL DEFAULT 0,
        CreateDate         datetime         NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate       datetime         NULL
    );
    CREATE UNIQUE INDEX UX_tblFloorPlanElement_ContentPackageId
        ON tblFloorPlanElement (ContentPackageId);
    PRINT 'Created tblFloorPlanElement';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblFloorPlanIconSchedule')
BEGIN
    CREATE TABLE tblFloorPlanIconSchedule (
        FloorPlanIconScheduleId int          NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblFloorPlanIconSchedule PRIMARY KEY,
        FloorPlanElementId      int          NOT NULL
            CONSTRAINT FK_tblFloorPlanIconSchedule_Element
                REFERENCES tblFloorPlanElement (FloorPlanElementId) ON DELETE CASCADE,
        AssetLibraryLocationId  int          NULL
            CONSTRAINT FK_tblFloorPlanIconSchedule_LibraryLocation
                REFERENCES tblLocation (LocationId) ON DELETE NO ACTION,
        MetadataKey             varchar(50)  NULL,
        MetadataValue           varchar(255) NULL,
        IconAssetId             int          NULL
            CONSTRAINT FK_tblFloorPlanIconSchedule_IconAsset
                REFERENCES tblAsset (AssetId) ON DELETE SET NULL,
        StartDate               datetime     NULL,
        EndDate                 datetime     NULL,
        StartTime               time(7)      NULL,
        EndTime                 time(7)      NULL,
        Sunday                  bit          NOT NULL DEFAULT 0,
        Monday                  bit          NOT NULL DEFAULT 0,
        Tuesday                 bit          NOT NULL DEFAULT 0,
        Wednesday               bit          NOT NULL DEFAULT 0,
        Thursday                bit          NOT NULL DEFAULT 0,
        Friday                  bit          NOT NULL DEFAULT 0,
        Saturday                bit          NOT NULL DEFAULT 0,
        SortOrder               int          NOT NULL DEFAULT 0,
        CreateDate              datetime     NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT 'Created tblFloorPlanIconSchedule';
END
GO

-- ============================================================
-- 9. Access schedules
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblAccessSchedule')
BEGIN
    CREATE TABLE tblAccessSchedule (
        AccessScheduleId     int              NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblAccessSchedule PRIMARY KEY,
        ContentPackageId     uniqueidentifier NOT NULL DEFAULT NEWID(),
        LocationAccessRuleId int              NOT NULL
            CONSTRAINT FK_tblAccessSchedule_AccessRule
                REFERENCES tblLocationAccessRule (LocationAccessRuleId) ON DELETE CASCADE,
        DisplayId            int              NULL
            CONSTRAINT FK_tblAccessSchedule_Display
                REFERENCES tblDisplay (DisplayId) ON DELETE NO ACTION,
        ConstraintId         int              NULL
            CONSTRAINT FK_tblAccessSchedule_Constraint
                REFERENCES tblConstraint (ConstraintId) ON DELETE NO ACTION,
        StartDate            datetime         NULL,
        EndDate              datetime         NULL,
        StartTime            time(7)          NULL,
        EndTime              time(7)          NULL,
        Sunday               bit              NOT NULL DEFAULT 0,
        Monday               bit              NOT NULL DEFAULT 0,
        Tuesday              bit              NOT NULL DEFAULT 0,
        Wednesday            bit              NOT NULL DEFAULT 0,
        Thursday             bit              NOT NULL DEFAULT 0,
        Friday               bit              NOT NULL DEFAULT 0,
        Saturday             bit              NOT NULL DEFAULT 0,
        CreateDate           datetime         NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate         datetime         NULL,
        LastEditUser         varchar(50)      NULL,
        MetadataKey          varchar(50)      NULL
    );
    CREATE UNIQUE INDEX UX_tblAccessSchedule_ContentPackageId
        ON tblAccessSchedule (ContentPackageId);
    PRINT 'Created tblAccessSchedule';
END
GO

-- ============================================================
-- 10. Content channels and packages
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblContentChannel')
BEGIN
    CREATE TABLE tblContentChannel (
        ChannelId    int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblContentChannel PRIMARY KEY,
        Name         nvarchar(100) NOT NULL,
        Description  nvarchar(500) NULL,
        LocationId   int           NULL
            CONSTRAINT FK_tblContentChannel_Location
                REFERENCES tblLocation (LocationId) ON DELETE SET NULL,
        DisplayId    int           NULL
            CONSTRAINT FK_tblContentChannel_Display
                REFERENCES tblDisplay (DisplayId) ON DELETE SET NULL,
        SourceType   nvarchar(50)  NOT NULL,
        DeviceId     varchar(50)   NULL
            CONSTRAINT FK_tblContentChannel_Device
                REFERENCES tblDevice (DeviceId) ON DELETE CASCADE,
        ConnectorId  int           NULL,
        ExternalId   nvarchar(200) NULL,
        ExternalName nvarchar(200) NULL,
        CreateDate   datetime      NOT NULL DEFAULT GETUTCDATE(),
        ExtendedData nvarchar(max) NULL
    );
    CREATE UNIQUE INDEX UX_tblContentChannel_DeviceId
        ON tblContentChannel (DeviceId) WHERE DeviceId IS NOT NULL;
    PRINT 'Created tblContentChannel';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblContentChannelMetadata')
BEGIN
    CREATE TABLE tblContentChannelMetadata (
        MetadataId    int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblContentChannelMetadata PRIMARY KEY,
        ChannelId     int           NOT NULL
            CONSTRAINT FK_tblContentChannelMetadata_Channel
                REFERENCES tblContentChannel (ChannelId) ON DELETE CASCADE,
        MetadataKey   nvarchar(50)  NOT NULL,
        MetadataValue nvarchar(255) NULL,
        CreateDate    datetime      NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE UNIQUE INDEX UX_tblContentChannelMetadata_ChannelKey
        ON tblContentChannelMetadata (ChannelId, MetadataKey);
    PRINT 'Created tblContentChannelMetadata';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblContentPackageHistory')
BEGIN
    CREATE TABLE tblContentPackageHistory (
        ContentPackageHistoryId int            NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblContentPackageHistory PRIMARY KEY,
        Direction               varchar(20)    NOT NULL,
        Status                  varchar(20)    NOT NULL,
        BundleFileName          nvarchar(255)  NULL,
        InitiatedByUser         nvarchar(255)  NULL,
        ExportedByUser          nvarchar(255)  NULL,
        SourceEnvironment       nvarchar(100)  NULL,
        ScopeDescription        nvarchar(500)  NULL,
        TotalRecords            int            NOT NULL DEFAULT 0,
        InsertedCount           int            NOT NULL DEFAULT 0,
        UpdatedCount            int            NOT NULL DEFAULT 0,
        SkippedCount            int            NOT NULL DEFAULT 0,
        StartedAt               datetime       NOT NULL DEFAULT GETUTCDATE(),
        CompletedAt             datetime       NULL,
        ErrorMessage            nvarchar(2000) NULL
    );
    CREATE INDEX IX_tblContentPackageHistory_StartedAt
        ON tblContentPackageHistory (StartedAt);
    PRINT 'Created tblContentPackageHistory';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblContentPackageHistoryItem')
BEGIN
    CREATE TABLE tblContentPackageHistoryItem (
        ContentPackageHistoryItemId int              NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblContentPackageHistoryItem PRIMARY KEY,
        ContentPackageHistoryId     int              NOT NULL
            CONSTRAINT FK_tblContentPackageHistoryItem_History
                REFERENCES tblContentPackageHistory (ContentPackageHistoryId) ON DELETE CASCADE,
        ContentPackageId            uniqueidentifier NOT NULL,
        EntityType                  varchar(50)      NOT NULL,
        Action                      varchar(20)      NOT NULL,
        SourceUserPrincipalName     nvarchar(255)    NULL,
        Notes                       nvarchar(500)    NULL
    );
    CREATE INDEX IX_tblContentPackageHistoryItem_HistoryId
        ON tblContentPackageHistoryItem (ContentPackageHistoryId);
    CREATE INDEX IX_tblContentPackageHistoryItem_ContentPackageId
        ON tblContentPackageHistoryItem (ContentPackageId);
    PRINT 'Created tblContentPackageHistoryItem';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblContentPackageEnvironmentLabel')
BEGIN
    CREATE TABLE tblContentPackageEnvironmentLabel (
        ContentPackageEnvironmentLabelId int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblContentPackageEnvironmentLabel PRIMARY KEY,
        Label                             nvarchar(100) NOT NULL,
        SortOrder                         int           NOT NULL DEFAULT 0,
        CreateDate                        datetime      NOT NULL DEFAULT GETUTCDATE(),
        CreatedByUser                     nvarchar(255) NULL
    );
    PRINT 'Created tblContentPackageEnvironmentLabel';
END
GO

-- ============================================================
-- 11. Video wall extensions
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblUserGroupLocationDefault')
BEGIN
    CREATE TABLE tblUserGroupLocationDefault (
        UserGroupLocationDefaultId int          NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblUserGroupLocationDefault PRIMARY KEY,
        UserGroupId                int          NOT NULL
            CONSTRAINT FK_tblUserGroupLocationDefault_UserGroup
                REFERENCES tblUserGroup (UserGroupId) ON DELETE CASCADE,
        LocationId                 int          NOT NULL
            CONSTRAINT FK_tblUserGroupLocationDefault_Location
                REFERENCES tblLocation (LocationId) ON DELETE CASCADE,
        DefaultConstraintId        int          NOT NULL
            CONSTRAINT FK_tblUserGroupLocationDefault_Constraint
                REFERENCES tblConstraint (ConstraintId) ON DELETE NO ACTION,
        BackgroundMode             nvarchar(20) NOT NULL DEFAULT 'fill-width',
        CreateDate                 datetime     NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate               datetime     NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE UNIQUE INDEX UX_tblUserGroupLocationDefault_GroupLocation
        ON tblUserGroupLocationDefault (UserGroupId, LocationId);
    PRINT 'Created tblUserGroupLocationDefault';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblWallLayoutMapping')
BEGIN
    CREATE TABLE tblWallLayoutMapping (
        WallLayoutMappingId int           NOT NULL IDENTITY(1,1)
            CONSTRAINT PK_tblWallLayoutMapping PRIMARY KEY,
        EndpointId          int           NOT NULL
            CONSTRAINT FK_tblWallLayoutMapping_Endpoint
                REFERENCES tblEndpoint (EndpointId) ON DELETE CASCADE,
        DisplayId           int           NOT NULL
            CONSTRAINT FK_tblWallLayoutMapping_Display
                REFERENCES tblDisplay (DisplayId) ON DELETE CASCADE,
        LayerRole           varchar(20)   NOT NULL DEFAULT 'status',
        PlatformData        nvarchar(max) NULL,
        CreateDate          datetime      NOT NULL DEFAULT GETUTCDATE(),
        LastEditDate        datetime      NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE INDEX IX_tblWallLayoutMapping_Endpoint ON tblWallLayoutMapping (EndpointId);
    CREATE INDEX IX_tblWallLayoutMapping_Display   ON tblWallLayoutMapping (DisplayId);
    PRINT 'Created tblWallLayoutMapping';
END
GO

-- ============================================================
-- 12. Column additions to existing tables
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('tblLocation') AND name = 'IsTransientCluster')
BEGIN
    ALTER TABLE tblLocation ADD IsTransientCluster bit NOT NULL
        CONSTRAINT DF_tblLocation_IsTransientCluster DEFAULT 0;
    PRINT 'Added tblLocation.IsTransientCluster';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('tblLocation') AND name = 'ParentVideoWallLocationId')
BEGIN
    ALTER TABLE tblLocation ADD ParentVideoWallLocationId int NULL;
    ALTER TABLE tblLocation ADD
        CONSTRAINT FK_tblLocation_ParentVideoWall
        FOREIGN KEY (ParentVideoWallLocationId) REFERENCES tblLocation (LocationId) ON DELETE NO ACTION;
    PRINT 'Added tblLocation.ParentVideoWallLocationId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('tblLocation') AND name = 'ClusterKey')
BEGIN
    ALTER TABLE tblLocation ADD ClusterKey varchar(100) NULL;
    PRINT 'Added tblLocation.ClusterKey';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('tblLocation') AND name = 'OwningUserGroupId')
BEGIN
    ALTER TABLE tblLocation ADD OwningUserGroupId int NULL;
    ALTER TABLE tblLocation ADD
        CONSTRAINT FK_tblLocation_OwningUserGroup
        FOREIGN KEY (OwningUserGroupId) REFERENCES tblUserGroup (UserGroupId) ON DELETE SET NULL;
    PRINT 'Added tblLocation.OwningUserGroupId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('tblScheduleEvent') AND name = 'UserGroupId')
BEGIN
    ALTER TABLE tblScheduleEvent ADD UserGroupId int NULL;
    ALTER TABLE tblScheduleEvent ADD
        CONSTRAINT FK_tblScheduleEvent_UserGroup
        FOREIGN KEY (UserGroupId) REFERENCES tblUserGroup (UserGroupId) ON DELETE SET NULL;
    PRINT 'Added tblScheduleEvent.UserGroupId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('tblScheduleEvent') AND name = 'OverrideConstraintId')
BEGIN
    ALTER TABLE tblScheduleEvent ADD OverrideConstraintId int NULL;
    ALTER TABLE tblScheduleEvent ADD
        CONSTRAINT FK_tblScheduleEvent_OverrideConstraint
        FOREIGN KEY (OverrideConstraintId) REFERENCES tblConstraint (ConstraintId) ON DELETE SET NULL;
    PRINT 'Added tblScheduleEvent.OverrideConstraintId';
END
GO

-- ============================================================
-- 13. Location type seed data (DXSuite-specific types)
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
PRINT 'Migration complete for SLC_DirectorDEV.';
PRINT 'Tables added: 40   Columns added: 6   Rows updated: see above';
PRINT 'No existing data was modified or deleted.';
PRINT '============================================================';
