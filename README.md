# LankaMart

A multi-vendor marketplace for Sri Lanka (Daraz-style), built as a set of .NET 10 microservices behind a YARP gateway.
Sellers register, pass KYC and list products; customers browse, check out with a (fake) card, and track orders.
The full build manual is in [`docs/build-guide.md`](docs/build-guide.md). Decisions are recorded in [`docs/adr/`](docs/adr).

## Architecture (Phase 1)

```
 Angular client (61501)     Flutter app (later)     Python recommender (later)
      │ (dev-server proxy /api)                              ▲
      └───────────┬──────────────────────────────────────────┘
                  ▼
        ┌─────────────────────────────┐
        │ GATEWAY (YARP) :5100        │   JWT check • rate limit • CORS • logout blacklist
        └──┬───────┬───────┬───────┬──┘   /api/v1/internal/**  →  NOT ROUTED (404)
 ┌─────────▼┐ ┌────▼────┐ ┌▼──────┐ ┌────▼────┐ ┌───────────┐ ┌───────────┐
 │ IDENTITY │ │  USERS  │ │CATALOG│ │ ORDERS  │ │ INVENTORY │ │ PAYMENTS  │
 │  :5101   │ │  :5102  │ │ :5103 │ │  :5105  │ │   :5104   │ │   :5106   │
 └────┬─────┘ └────┬────┘ └───┬───┘ └──┬──────┘ └─────┬─────┘ └─────┬─────┘
      └────────────┴──────────┴───┬────┴──────────────┴──────────────┘
                 ONE Postgres, six schemas, six logins
        RabbitMQ (events)     Redis (stock, carts, OTP, idempotency)
 Orders ──internal REST──► Catalog (price lookup) · Inventory (reserve) · Payments (intent)
```

## Prerequisites

.NET SDK 10.0.x · Docker Desktop (running) · Node.js LTS · `dotnet tool install --global dotnet-ef --version "10.*"` ·
Python 3.10+ (only for `scripts/seed_demo.py`). Verified versions: [`docs/environment.md`](docs/environment.md).

## Quick start

**Option A — Aspire (recommended, replaces the guide's `scripts/dev.py`):**

```bash
dotnet run --project E_Com_App.AppHost          # or open E_Com_App.slnx in Visual Studio and press F5
```

The AppHost starts Postgres/Redis/RabbitMQ containers, every service on its fixed port, and the Angular client.
Open the dashboard link it prints for logs, traces and health.

**Option B — plain Docker + dotnet:**

```bash
cp .env.example .env
docker compose up -d                                              # infra only
dotnet run --project src/services/identity/LankaMart.Identity.Api # …one terminal per service
```

**Everything in containers (only the gateway is published):**

```bash
docker compose -f docker-compose.yml -f docker-compose.apps.yml up -d --build
```

> Option A and Option B publish the same host ports — run one at a time (`docker compose down` before starting the AppHost).

**Demo data:** `pip install requests && python scripts/seed_demo.py` (talks only to the gateway on `:5100`).

## Ports

| Component | Port | | Component | Port |
|---|---|---|---|---|
| **Gateway** | **5100** | | Orders | 5105 |
| Identity | 5101 | | Payments | 5106 |
| Users | 5102 | | Angular dev server | https://localhost:61501 |
| Catalog | 5103 | | Postgres | **5433** (host) → 5432 |
| Inventory | 5104 | | Redis / RabbitMQ (UI) | 6379 / 5672 (15672) |

Postgres is published on **5433** because this machine already runs a native PostgreSQL on 5432.
Dev credentials (all throwaway, local-only) are in `docs/build-guide.md` Appendix B.

## Build & test

```bash
dotnet build LankaMart.Backend.slnf
dotnet test  LankaMart.Backend.slnf      # integration tests need Docker (Testcontainers)
```

## Docs

- [`docs/build-guide.md`](docs/build-guide.md) — the Phase 0/1 build manual this repo follows
- [`docs/adr/`](docs/adr) — architecture decision records
- [`docs/known-gaps.md`](docs/known-gaps.md) — deliberate Phase 1 shortcuts and when they get fixed
