# ADR-0005: One Postgres, one schema + one login per service
Date: 2026-11-11 · Status: Accepted
## Context
Principle #1: no service reads another service's database. A schema alone is only a folder.
## Decision
`infra/postgres/init/01-init.sql` creates schema `<svc>` owned by role `<svc>_svc`; each service connects with its own role and cannot see other schemas.
## Alternatives
- Shared `postgres` login (rule lives only in our heads) · one database server per service now (too heavy for a laptop).
## Consequences
+ Isolation is physically enforced; the Phase 2 split is a connection-string change. − Cross-service data needs events/read models, never joins.
