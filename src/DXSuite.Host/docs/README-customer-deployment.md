# DXSuite Director + NetJets — On-Premises Database Deployment

This package provisions the SQL Server database for an on-premises DXSuite
deployment running the **Director** and **NetJets** modules only.

## Contents

| File | Purpose |
|------|---------|
| `director-provisioning.sql` | Creates the `DXSuite_Director` database, schema, indexes, foreign keys, and system lookup data. Idempotent. |
| `seed-netjets.sql` | Customer-specific seed data: location groups, locations, and 51 displays. Run **after** provisioning. Idempotent. |
| `README-customer-deployment.md` | This document. |

## Prerequisites

**SQL Server**
- SQL Server 2019 or later (Standard or Enterprise; Express is supported for small deployments)
- Database collation: `SQL_Latin1_General_CP1_CI_AS` (the provisioning script creates the database with this collation)
- Account running the provisioning scripts: **sysadmin**, or at minimum `CREATE DATABASE` on the server and `db_owner` on `DXSuite_Director` once created

**Application host**
- Windows Server 2019 or later, or any Linux distribution supported by .NET 10
  - The Windows Server 2019 floor is an ICU requirement, not a .NET one: DXSuite resolves every
    schedule against IANA time zone identifiers such as `America/Chicago`, and earlier Windows
    releases cannot. The Host refuses to start if it finds it cannot resolve them.
- .NET 10 ASP.NET Core runtime installed
- Outbound HTTPS access if you intend to use cloud-based identity providers (Microsoft Entra ID, Google, Okta)

## Deployment Steps

### 1. Run the provisioning script

Connect to the target SQL Server instance as a sysadmin login and run:

```
sqlcmd -S <server> -E -i director-provisioning.sql
```

Or open the script in SQL Server Management Studio / Azure Data Studio and execute against the `master` database.

The script creates `DXSuite_Director` if it does not already exist, then creates all tables, indexes, foreign keys, and seed lookup data. It is fully idempotent, re-running against an existing database is safe.

### 2. Create the application service account

Create one per your organization's policy.

**Required permissions:** `db_datareader` and `db_datawriter` on `DXSuite_Director`. No other permissions are required.

### 3. Run the seed script

After the provisioning script completes successfully, run:

```
sqlcmd -S <server> -E -d DXSuite_Director -i seed-netjets.sql
```

This inserts the location groups, locations, and displays. The script verifies that required system lookup rows (e.g., `LocationTypeId` 12 and 1100) exist and aborts with a clear error if the provisioning script has not been run.

## Troubleshooting

| Symptom | Cause / Fix |
|---------|-------------|
| Seed script throws `LocationTypeId 12 is missing` | The provisioning script has not been run, or did not complete. Re-run `director-provisioning.sql`. |
| Provisioning script reports `database collation mismatch` | The database existed before this script ran with a different collation. Drop and re-create, or migrate collation per Microsoft's documentation. |

## Scope

This README covers **database provisioning** only. Application installation, IIS configuration, reverse proxy setup, identity provider configuration, media storage paths, and TLS certificates are covered in the separate **DXSuite Installation Guide**.
