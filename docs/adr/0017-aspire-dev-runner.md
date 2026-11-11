# ADR-0017: .NET Aspire AppHost as the dev runner
Date: 2026-11-11 · Status: Accepted
## Context
The build guide uses `scripts/dev.py` to start Docker infra and every service. This repo started from a Visual Studio Aspire template, and the team wants to keep Aspire.
## Decision
`E_Com_App.AppHost` replaces `dev.py`: it runs Postgres 17 (with the shared init SQL), plain Redis 7, RabbitMQ 4 (+ management), all .NET services on the guide's **fixed, unproxied** ports, and the Angular client. Services export OTLP to the Aspire dashboard when `Otel:Endpoint` is empty (Jaeger stays optional).
## Alternatives
- `scripts/dev.py` exactly as in the guide · docker-compose for everything.
## Consequences
+ One F5 starts the system; dashboard shows logs, traces and health. − `docker-compose.yml` publishes the same ports, so run **either** compose **or** the AppHost. Postgres is published on host port **5433** because this machine already runs a native PostgreSQL on 5432.
