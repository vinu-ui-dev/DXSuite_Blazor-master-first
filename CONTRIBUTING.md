# Contributing to DXSuite

This is the one place for how we work on this repo. See the README for architecture and
deployment models, and `db/schema-migration-strategy.md` for the database rules in depth.

## Getting started

- Install the .NET 10 SDK and SQL Server (local or a dev instance).
- Set up your local secrets (see Secrets below). Do this before running.
- Build: `dotnet build DXSuite.sln`
- Run the app: `dotnet run --project src/DXSuite.Host` (or start `DXSuite.Host` from the IDE).

## Branching and pull requests

`master` is protected. Nobody pushes to it directly; changes land through a pull request.

- Branch off `master`: `feature/<short-name>`, `fix/<short-name>`, or `chore/<short-name>`.
- Keep pull requests small and focused, and write what changed and why in the description.
- CI must be green before merge (build plus the schema-drift check).
- One approving review is required to merge.
- Squash-merge to keep `master` history readable.

## Secrets and configuration

- Never commit secrets. The committed `appsettings` files are sterilized on purpose.
- Local development uses .NET user-secrets, per developer. The key list and setup are in the
  README; run the provided secrets setup script after cloning.
- In the cloud, secrets come from Azure Key Vault and load automatically when `KeyVault:Uri`
  is configured. Absent that key, the app falls back to local configuration.

## Database schema changes

Schema is owned by EF Core migrations. This is the rule that keeps databases from drifting.

- Change the entity or model configuration, then generate a migration for that context:
  `dotnet ef migrations add <Name> --project src/<Project> --context <DbContext>`.
- Never hand-apply DDL to a database, and never edit `__EFMigrationsHistory` by hand.
- Provisioning scripts create the database and load seed data only. Do not put table DDL in
  them; add a migration instead.
- Before opening a PR, confirm no drift: `dotnet ef migrations has-pending-model-changes
  --context <DbContext>`. CI runs this for every owned context and fails a PR that changed a
  model without adding a migration.

## Code style

- Formatting is defined in `.editorconfig` and most editors apply it automatically.
- Run `dotnet format` before pushing if your editor did not.
- Match the surrounding code. Keep comments focused on why, not what.

## Commits

- Write clear messages: a short summary line, then what and why if it needs explaining.
- Reference the issue or PR when relevant.
