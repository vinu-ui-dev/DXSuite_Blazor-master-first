# Schema Migration Strategy

Status: decided, not yet implemented. Captures how DXSuite databases are versioned and evolved
across the team and across tenants.

## Decisions

1. One mechanism: EF Core migrations for every database we own. Retire hand-run SQL patches.
2. Exclude external databases: the CoreData/Aviation database is DB-first scaffolded and owned by an
   external system. We never issue DDL against it; we track its changes by re-scaffolding the model.
3. One source of truth, two delivery modes: migrations are authoritative. In cloud we auto-apply via
   a tenant fan-out migrator. For on-prem we can emit idempotent SQL
   (`dotnet ef migrations script --idempotent`) for a customer DBA to review and run. Both modes are
   supported, because on-prem customers may want either.

## Why CoreData and Aviation are excluded

They are two EF DbContexts over the same physical database, each with its own named connection
string and its own per-tenant string on ITenantContext. CoreData maps general reference data
(languages, weather, event logs, API keys, system health). Aviation maps the airport domain
(airlines, flights, gates, carousels, baggage, plus reporting views). Both are DB-first scaffolded:
constraint names like `PK__tblCount__5D9B0D2DBCCAA3FC`, legacy lowercase table names, and `ToView`
mappings. We consume this database; we do not own its schema. The two connection strings normally
point at the same DB; keep both and document it, since it preserves the option to split Aviation onto
its own DB per tenant later.

## Database inventory

| Database | Owner | Model style | Today | Plan |
|---|---|---|---|---|
| Platform | us | code-first | EF migrations | Done. Provisioning regenerated from migrations (idempotent). |
| Design | us | code-first | EF migrations (re-baselined to InitialCreate) | Done. History was tangled; squashed and dev DB stamped. |
| Director | us | DB-first scaffolded | EF migrations (InitialCreate baseline, 76 tables) | Baseline done; matches prod schema table-for-table. Provisioning slimmed to bootstrap + db/scripts/director-seed.sql. Per-env verify+stamp pending. Fan-out is cloud-future. |
| TenantRegistry | us | code-first | EF migrations (InitialCreate baseline) | Done. Cloud-only; bootstrap is DB+login. Stamp script if a live registry DB exists. |
| Sentinel | us | code-first | EF migrations (InitialCreate baseline) | Done. Runtime-disabled; dev DB stamp script provided (or recreate clean). |
| SLC/Pulse | external | scaffolded | hand-run SQL patches | Exclude. External and being deprecated. |
| CoreData/Aviation | external | DB-first scaffolded | not ours | Exclude. Re-scaffold only. |

## The two hard parts

### Per-tenant fan-out (Director)
A migrator that reads every tenant connection string from TenantRegistry and brings each tenant
Director DB to head, idempotently. It logs per-DB results, and one tenant's failure must not block the
others. The same "new empty DB to head" path is also tenant onboarding.

### Baselining an existing scaffolded database
Director already exists in production across many tenant DBs, so EF migrations cannot start by
creating tables. Adoption steps:
1. Generate InitialCreate from the current scaffolded model.
2. For existing DBs, mark InitialCreate as already applied (insert the `__EFMigrationsHistory` row) so
   EF does not try to recreate the schema.
3. For new tenant DBs, run InitialCreate plus all later migrations from empty. This replaces
   director-provisioning.sql.
4. Every schema change after baseline is a new migration, fanned out to all tenant DBs.

## Where migrations run

- Cloud (web/worker roles): never on web-instance startup, to avoid DDL races across instances. Run
  as an explicit deploy step, a one-shot migrator job, or in the worker role only.
- Standalone on-prem (single instance): may run on startup, or emit an idempotent SQL script for the
  customer DBA. Both are supported per the delivery-mode decision.

## Escape hatch for large tables

Use `migrationBuilder.Sql(...)` inside a migration for online index rebuilds, batched backfills, or
any change where EF's generated DDL would be naive about locking. This keeps per-DB tracking and
fan-out while preserving full SQL control where it matters, which removes the main reason to reach for
a separate SQL-script runner.

## Cleanup and reconciliation

- Duplicate to resolve: LaborColumnsRedesign exists as both EF migration 20260426144040 and
  db/migrations/LaborColumnsRedesign_Migration.sql. Keep the EF migration, archive the SQL.
- Retire Design's loose phase-k/m/n SQL once represented in migrations.
- Provisioning scripts become bootstrap-from-migrations. Archive the hand-run patch scripts as
  historical record rather than deleting them.

## New-tenant onboarding

Provisioning a new tenant becomes: create an empty Director DB, then migrate to head. This is the same
code path as the fan-out migrator's new-DB branch, so onboarding and upgrade share one mechanism.

## Working agreement (how we avoid drift)

This is the rule set that keeps a database from drifting away from its migrations, which is exactly
what happened to Design before it was re-baselined.

- Schema changes go through EF migrations. Change the entity or model configuration, then
  `dotnet ef migrations add <Name>` for that context. Never hand-apply DDL to a database, and never
  hand-edit `__EFMigrationsHistory`.
- One source of truth per database. Provisioning scripts create the database and the application
  login only. They do not define tables. Table DDL belongs in a migration.
- Before a PR that changes an owned DB model, run
  `dotnet ef migrations has-pending-model-changes --context <Context>`. A non-clean result means a
  migration is missing from the PR.
- Each owned context has a design-time factory (PlatformDbContextFactory, DesignDbContextFactory) so
  the tooling runs without booting the Host pipeline. New owned contexts get one too.
- Generated upgrade scripts (`dotnet ef migrations script --idempotent`) are derived artifacts, not
  hand-maintained. Regenerate them; do not edit them by hand.
