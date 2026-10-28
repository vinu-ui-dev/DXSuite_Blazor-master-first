-- =============================================================================
-- One-time: bring an EXISTING Tenant Registry database under EF migration tracking.
--
-- Only needed if a TenantRegistry database already exists (cloud deployments). Dev typically uses
-- the config tenant store and has no registry DB; in that case skip this. A fresh registry DB is
-- created by the bootstrap script and brought to schema with `dotnet ef database update`.
--
-- Run only against a registry DB whose schema already matches the model (the two hand-provisioned
-- tables tblTenant and tblApiKeyMapping). NO TABLE DDL RUNS.
-- =============================================================================
USE DXSuite_TenantRegistry;
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId]    nvarchar(150) NOT NULL CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY,
        [ProductVersion] nvarchar(32)  NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260622135456_InitialCreate')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260622135456_InitialCreate', N'10.0.7');
GO

PRINT 'Tenant Registry DB stamped with InitialCreate baseline.';
GO
