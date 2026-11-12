# ADR-0019: Database per service; migrations as a `--migrate-only` job
Date: 2026-11-12 · Status: Accepted (guide ADR-017; supersedes the shared-database part of ADR-0005)
## Context
Phase 1 used one database with a schema + login per service (ADR-0005). A runaway query or a migration lock in one schema can still stall the others, backups/restores are all-or-nothing, and no service can move to its own server. Separately, every replica ran `Migrate()` at start-up — with several replicas they race, and a bad migration only shows up after traffic has moved.
## Decision
`infra/postgres/init/01-databases.sh` creates one **database** `<svc>_db` owned by role `<svc>_svc` per service and revokes `CONNECT` from `PUBLIC` (still one Postgres server in dev/staging). Each service changes only `Database=` in its connection string. Migrations run through `RunMigrationsIfRequestedAsync`: auto-migrate only in Development (`Database:AutoMigrate`), and `dotnet X.dll --migrate-only` migrates then exits — run as a Kubernetes/Argo **PreSync Job** from the same image.
## Alternatives
- Keep schemas (weaker isolation, no independent moves) · one Postgres server per service now (too heavy for a laptop) · a separate migration tool/image (Flyway, EF bundles — more artefacts).
## Consequences
+ A login cannot even connect to a neighbour's database (`permission denied for database`); each database can later move to its own server; migrations run once, before rollout, and fail loudly. − Init scripts only run on an **empty** volume (`docker compose down -v`; the Aspire volume was renamed to `lankamart-aspire-pgdata-p2`). − `pg_trgm` must be created by the superuser inside `catalog_db`.
