BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622155537_AddEnrollmentToken'
)
BEGIN
    CREATE TABLE [SentinelEnrollmentToken] (
        [TokenID] int NOT NULL IDENTITY,
        [CustomerID] varchar(20) NOT NULL,
        [Label] varchar(100) NOT NULL,
        [TokenHash] varchar(100) NOT NULL,
        [DefaultRoles] varchar(100) NULL,
        [ExpiresUtc] datetime NULL,
        [MaxUses] int NULL,
        [UseCount] int NOT NULL,
        [IsRevoked] bit NOT NULL,
        [CreateDate] datetime NOT NULL,
        [CreatedBy] varchar(50) NULL,
        CONSTRAINT [PK_SentinelEnrollmentToken] PRIMARY KEY ([TokenID])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622155537_AddEnrollmentToken'
)
BEGIN
    CREATE INDEX [IX_SentinelEnrollmentToken_CustomerID] ON [SentinelEnrollmentToken] ([CustomerID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622155537_AddEnrollmentToken'
)
BEGIN
    CREATE INDEX [IX_SentinelEnrollmentToken_TokenHash] ON [SentinelEnrollmentToken] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622155537_AddEnrollmentToken'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260622155537_AddEnrollmentToken', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622160253_AddGatewayEnrollmentFingerprint'
)
BEGIN
    ALTER TABLE [SentinelSiteGateway] ADD [EnrollmentFingerprint] varchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622160253_AddGatewayEnrollmentFingerprint'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260622160253_AddGatewayEnrollmentFingerprint', N'10.0.7');
END;

COMMIT;
GO

