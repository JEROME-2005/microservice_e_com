# ADR-0003: MassTransit v8.x (pinned) over RabbitMQ
Date: 2026-11-11 · Status: Accepted
## Context
MassTransit v9 is commercial; v8 stays Apache-2.0 but official maintenance ends after 2026.
## Decision
Pin `MassTransit.RabbitMQ` to `8.*`. Keep consumers thin so a migration is mechanical.
## Alternatives
- MassTransit v9 (paid) · Wolverine · Rebus · raw RabbitMQ.Client.
## Consequences
+ Free, well documented, matches tutorials. − **Decision point before launch (Phase 7):** pay for v9 or migrate to an OSS alternative.
