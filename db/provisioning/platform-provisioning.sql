IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE TABLE [tblPlatformAuditLog] (
        [AuditId] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [EventTime] datetime2 NOT NULL DEFAULT ((getutcdate())),
        [PlatformUserId] uniqueidentifier NULL,
        [TenantId] nvarchar(100) NULL,
        [EventType] nvarchar(50) NOT NULL,
        [ResourcePath] nvarchar(512) NULL,
        [TicketRef] nvarchar(100) NULL,
        [Reason] nvarchar(1000) NULL,
        [DetailsJson] nvarchar(max) NULL,
        CONSTRAINT [PK_tblPlatformAuditLog] PRIMARY KEY ([AuditId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE TABLE [tblPlatformRole] (
        [RoleId] int NOT NULL IDENTITY,
        [RoleName] nvarchar(50) NOT NULL,
        [Description] nvarchar(256) NULL,
        CONSTRAINT [PK_tblPlatformRole] PRIMARY KEY ([RoleId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE TABLE [tblPlatformUser] (
        [PlatformUserId] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [PrimaryEmail] nvarchar(256) NOT NULL,
        [DisplayName] nvarchar(100) NULL,
        [IsPlatformOperator] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL DEFAULT ((getutcdate())),
        [LastLoginDate] datetime2 NULL,
        CONSTRAINT [PK_tblPlatformUser] PRIMARY KEY ([PlatformUserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE TABLE [tblTenantAccessNotice] (
        [NoticeId] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [TenantId] nvarchar(100) NOT NULL,
        [EventTime] datetime2 NOT NULL DEFAULT ((getutcdate())),
        [OperatorDisplayName] nvarchar(100) NULL,
        [EventType] nvarchar(50) NOT NULL,
        [ElevationId] uniqueidentifier NULL,
        [TicketRef] nvarchar(100) NULL,
        [Reason] nvarchar(1000) NULL,
        [ResourcePath] nvarchar(512) NULL,
        CONSTRAINT [PK_tblTenantAccessNotice] PRIMARY KEY ([NoticeId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE TABLE [tblTenantSettings] (
        [TenantSettingsId] int NOT NULL IDENTITY,
        [TenantId] nvarchar(100) NOT NULL,
        [RequireTenantApprovalForElevation] bit NOT NULL,
        [ElevationMaxDurationMinutes] int NULL,
        [NotifyOnElevationEmailsJson] nvarchar(2000) NULL,
        [BlockedRegionsJson] nvarchar(500) NULL,
        CONSTRAINT [PK_tblTenantSettings] PRIMARY KEY ([TenantSettingsId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE TABLE [tblPlatformElevation] (
        [ElevationId] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [PlatformUserId] uniqueidentifier NOT NULL,
        [TenantId] nvarchar(100) NOT NULL,
        [RoleName] nvarchar(50) NOT NULL,
        [TicketRef] nvarchar(100) NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [Status] nvarchar(20) NOT NULL DEFAULT N'Pending',
        [IssuedAt] datetime2 NOT NULL DEFAULT ((getutcdate())),
        [ExpiresAt] datetime2 NOT NULL,
        [RequestedByPlatformUserId] uniqueidentifier NOT NULL,
        [ApprovedByPlatformUserId] uniqueidentifier NULL,
        [RevokedAt] datetime2 NULL,
        [RevokedByPlatformUserId] uniqueidentifier NULL,
        [TenantApprovedAt] datetime2 NULL,
        CONSTRAINT [PK_tblPlatformElevation] PRIMARY KEY ([ElevationId]),
        CONSTRAINT [FK_PlatformElevation_User] FOREIGN KEY ([PlatformUserId]) REFERENCES [tblPlatformUser] ([PlatformUserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE TABLE [tblPlatformUserRole] (
        [PlatformUserRoleId] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [PlatformUserId] uniqueidentifier NOT NULL,
        [RoleId] int NOT NULL,
        [GrantedByPlatformUserId] uniqueidentifier NULL,
        [GrantedDate] datetime2 NOT NULL DEFAULT ((getutcdate())),
        CONSTRAINT [PK_tblPlatformUserRole] PRIMARY KEY ([PlatformUserRoleId]),
        CONSTRAINT [FK_PlatformUserRole_Role] FOREIGN KEY ([RoleId]) REFERENCES [tblPlatformRole] ([RoleId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PlatformUserRole_User] FOREIGN KEY ([PlatformUserId]) REFERENCES [tblPlatformUser] ([PlatformUserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE TABLE [tblPlatformUserTenantMembership] (
        [MembershipId] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [PlatformUserId] uniqueidentifier NOT NULL,
        [TenantId] nvarchar(100) NOT NULL,
        [TenantUserId] int NOT NULL,
        [IsActive] bit NOT NULL,
        [GrantedDate] datetime2 NOT NULL DEFAULT ((getutcdate())),
        [GrantedByPlatformUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_tblPlatformUserTenantMembership] PRIMARY KEY ([MembershipId]),
        CONSTRAINT [FK_PlatformMembership_PlatformUser] FOREIGN KEY ([PlatformUserId]) REFERENCES [tblPlatformUser] ([PlatformUserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'Description', N'RoleName') AND [object_id] = OBJECT_ID(N'[tblPlatformRole]'))
        SET IDENTITY_INSERT [tblPlatformRole] ON;
    EXEC(N'INSERT INTO [tblPlatformRole] ([RoleId], [Description], [RoleName])
    VALUES (1, N''Manage tenants, operators, and billing. Cannot read tenant business data.'', N''PlatformOperator''),
    (2, N''Time-bound read access into a specific tenant for support purposes.'', N''TenantSupport''),
    (3, N''Time-bound read+write access into a specific tenant. Requires second approver.'', N''TenantSupportWrite''),
    (4, N''Read-only access to platform audit logs across all tenants. No tenant data access.'', N''Auditor'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'Description', N'RoleName') AND [object_id] = OBJECT_ID(N'[tblPlatformRole]'))
        SET IDENTITY_INSERT [tblPlatformRole] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE INDEX [IX_PlatformElevation_UserTenantExpiry] ON [tblPlatformElevation] ([PlatformUserId], [TenantId], [ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_PlatformRole_RoleName] ON [tblPlatformRole] ([RoleName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_PlatformUser_PrimaryEmail] ON [tblPlatformUser] ([PrimaryEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE INDEX [IX_tblPlatformUserRole_RoleId] ON [tblPlatformUserRole] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_PlatformUserRole] ON [tblPlatformUserRole] ([PlatformUserId], [RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_PlatformMembership_UserTenant] ON [tblPlatformUserTenantMembership] ([PlatformUserId], [TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE INDEX [IX_TenantAccessNotice_TenantTime] ON [tblTenantAccessNotice] ([TenantId], [EventTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_TenantSettings_TenantId] ON [tblTenantSettings] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417182721_InitialPlatform'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417182721_InitialPlatform', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417191116_AddTenantAdminFlag'
)
BEGIN
    ALTER TABLE [tblPlatformUserTenantMembership] ADD [IsTenantAdmin] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417191116_AddTenantAdminFlag'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417191116_AddTenantAdminFlag', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512152208_MakeTenantUserIdNullable'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[tblPlatformUserTenantMembership]') AND [c].[name] = N'TenantUserId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [tblPlatformUserTenantMembership] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [tblPlatformUserTenantMembership] ALTER COLUMN [TenantUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260512152208_MakeTenantUserIdNullable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260512152208_MakeTenantUserIdNullable', N'10.0.7');
END;

COMMIT;
GO

