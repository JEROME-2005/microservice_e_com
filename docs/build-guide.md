# LankaMart — Phase 0 & Phase 1: The Complete Beginner Build Guide

**Project:** LankaMart (multi-vendor marketplace for Sri Lanka — a Daraz-style platform)
**Covers:** Phase 0 "Foundation" (Weeks 1–2) and Phase 1 "Core Buying Loop" (Weeks 3–10)
**Your stack:** .NET (C#) · PostgreSQL · Redis · RabbitMQ · React · Flutter · Python (algorithms) · REST (+ SOAP where truly needed)
**Written:** 9 November 2026 · **Source documents:** *Basic Architecture*, *The Full Architecture*, *System Scenarios*

---

## HOW TO USE THIS DOCUMENT

This is a **build manual**, not an essay. Work through it top to bottom. Every step has the same shape:

| Marker | Meaning |
|---|---|
| 🎯 **Goal** | What you will have when the step is done |
| 🧠 **Why** | The reasoning — read this, it is how you learn architecture instead of copying code |
| 🛠️ **Do** | Exact commands and code |
| ✅ **Check** | How to prove it works before you move on |
| ⚠️ **Pitfalls** | The mistakes beginners (and everyone) actually make here |

**Five rules for a beginner:**

1. **Type the code, don't paste it.** Typing forces you to read every line. Paste only boilerplate (config files).
2. **One step → one commit.** If something breaks, `git diff` tells you exactly what you changed.
3. **Never move on while a ✅ Check fails.** Bugs compound across 7 services; a failed check today is a lost weekend next week.
4. **The code here is a reference implementation.** If the IDE complains about a missing `using`, press `Ctrl+.` (VS/VS Code) or `Alt+Enter` (Rider) and accept the suggestion. Package version numbers are deliberately *not* pinned in this guide — `dotnet add package` fetches the current stable one (exceptions are called out in red-flag boxes).
5. **When stuck for more than 45 minutes:** (a) read the exact error text, (b) check the service log, (c) search the error text in quotes, (d) write down what you expected vs. what happened, (e) ask. Do not "try random things".

**Realistic time budget.** The plan assumes **15–20 focused hours per week**. As a beginner, expect it to take **1.5×** the calendar time (≈ 15 weeks instead of 10 for Phase 1). That is normal and fine — the roadmap is built so every phase ends with something that works. A **cut list** (what to drop if you fall behind) is in §1.17.

---

## TABLE OF CONTENTS

**PART A — Decisions & corrections to the architecture documents**
**PART B — PHASE 0: Foundation (Weeks 1–2)**
0.1 Hardware & accounts · 0.2 Install the tools · 0.3 Git & GitHub · 0.4 Monorepo skeleton · 0.5 The .NET solution · 0.6 Infrastructure with Docker Compose · 0.7 BuildingBlocks (shared toolbox) · 0.8 Empty services + YARP gateway · 0.9 Learn Docker: containerise a service · 0.10 CI pipeline · 0.11 Dev runner & ports · 0.12 Docs & ADRs · 0.13 Phase 0 gate · 0.14 Troubleshooting

**PART C — PHASE 1: Core Buying Loop (Weeks 3–10)**
1.1 Goal, scope, and what "done" means · 1.2 Concepts you must understand first · 1.3 Phase 1 architecture · 1.4 Data ownership & database design · 1.5 Shared contracts (events) · 1.6 Identity service · 1.7 Gateway hardening · 1.8 User service · 1.9 Catalog service · 1.10 Inventory service · 1.11 Order service · 1.12 Payment service (mock gateway) · 1.13 React storefront · 1.14 Flutter customer app (slice) · 1.15 Python recommender · 1.16 Testing · 1.17 Week-by-week plan & cut list · 1.18 Security checklist · 1.19 Run everything in Docker · 1.20 Phase 1 gate & demo script · 1.21 Troubleshooting · 1.22 What comes next (Phase 2 preview)

**APPENDICES** A Ports & URLs · B Dev credentials & config · C REST endpoint reference · D Event catalog · E Command cheat-sheet · F SOAP: when and how · G Glossary · H Decision log

---

# PART A — DECISIONS & CORRECTIONS TO THE ARCHITECTURE DOCUMENTS

Your architecture documents are good. But they were written before some facts changed, and some choices are too heavy for a first-time builder. I checked the current state of the ecosystem (as of **9 Nov 2026**) and adjusted. Every deviation is listed here so nothing surprises you later.

| # | Topic | Architecture doc said | This guide does | Why |
|---|---|---|---|---|
| 1 | **.NET version** | .NET 8 | **.NET 10 (LTS)** | .NET 8 and .NET 9 reach **end of support on 10 Nov 2026** — tomorrow. .NET 10 (released Nov 2025) is supported to Nov 2028. Starting a new project on an expiring runtime would be a mistake. Everything in the doc (YARP, EF Core, minimal APIs, OpenTelemetry) works the same. |
| 2 | **MediatR / AutoMapper** | MediatR for CQRS-lite | **Not used.** Plain handler classes + manual mapping | Both moved to commercial licensing. For a solo learner it adds a dependency without adding understanding. CQRS-lite is still achieved by structuring code as small "feature" classes. |
| 3 | **MassTransit** | MassTransit + RabbitMQ | **MassTransit v8.x (pinned, free, Apache-2.0)** + RabbitMQ | MassTransit **v9 is commercial**; v8 stays open source but official maintenance ends after 2026. For learning, v8 is perfect (most tutorials match it). **Decision point before launch (Phase 7):** pay for v9, or migrate to an OSS alternative (Wolverine, Rebus). We keep consumers thin so migration is mechanical. See ADR-003. |
| 4 | **Identity** | OpenIddict (full OIDC server) | **Hand-built JWT + refresh-token service on ASP.NET Core Identity** in Phase 1; optional OpenIddict migration in Phase 6 | OpenIddict needs an interactive login UI for the recommended Authorization-Code flow — a lot to learn at once. Building token issuing by hand (RS256 JWT, refresh rotation, reuse detection, JWKS) teaches you *what OpenIddict automates*. The public API (`/api/v1/auth/*`) stays identical, so clients never change. |
| 5 | **Object storage** | MinIO | **Not needed in Phase 1** (product images are URLs). Pick **SeaweedFS or Garage** (S3-compatible) in Phase 3 | The MinIO community project **was archived (read-only) in April 2026** and no longer ships images/security fixes. Code written against the S3 API (`AWSSDK.S3`) works with any replacement. |
| 6 | **Search** | Meilisearch from the start | **PostgreSQL `ILIKE` + `pg_trgm`** in Phase 1; Meilisearch in Phase 3 | Phase 1 is single-language. The roadmap itself says multilingual analyzers land in Phase 3. Don't run infrastructure you can't use yet. |
| 7 | **Checkout saga** | MassTransit state-machine saga | **Explicit orchestration**: order row = saga state, atomic compare-and-set transitions, expiry sweeper | A hand-written process manager is easier to read, debug and test for a beginner, and teaches the *concept* (state + compensation) without the state-machine DSL. It is a legitimate orchestration saga. Optional refactor to a MassTransit saga after Phase 2 as a learning exercise. |
| 8 | **Reservation expiry** | Redis sorted set sweeper | **Postgres query sweeper** | Postgres is the durable truth; if Redis restarts you must not lose track of which reservations to release. Redis still holds the hot counters. |
| 9 | **SOAP** | "use SOAP if needed" | **Not needed in Phase 1.** Appendix F shows exactly when and how to consume a SOAP service | Everything in your stack (PayHere, FCM, SMS gateways) is REST/JSON or form-POST. SOAP appears only with *legacy* bank/government/courier systems. |
| 10 | **Database** | one Postgres, schema-per-service | **Same, plus one DB login (role) per service** | A schema is just a folder; a *role* that can only see its own schema is the lock on the door. This enforces Principle #1 ("no service reads another service's DB") physically, and makes the Phase 2 split trivial. |
| 11 | **Service-to-service auth** | not specified | **Shared internal API key** header on `/internal/*` endpoints; those routes are never exposed by the gateway | Simple and good enough for Phase 1. Replaced by mTLS/service mesh in Phase 6. |
| 12 | **React** | React (SSR later for SEO) | **Vite + React + TypeScript SPA** in Phase 1 | SEO/SSR matters from Phase 3. Because the backend is a pure API, moving the UI to Next.js/React Router framework mode later does not touch the backend. |
| 13 | **Phase 1 Flutter** | Flutter customer app (Phase 7 polish) | **A thin slice** (login + browse + product detail) | Proves the same API serves mobile. Full app comes later. |
| 14 | **Python** | "for algorithms" | **FastAPI recommender service** (related-products + behaviour-scoring functions) | Gives your recommendation algorithm (System Scenarios §2.2) a real home from day one. |

> **Red-flag box — package versions you must NOT take blindly**
> * `MassTransit` / `MassTransit.RabbitMQ` → always add with `--version "8.*"`. If you run plain `dotnet add package MassTransit` you will get **v9 (commercial)**.
> * Do not add `MediatR`, `AutoMapper`, `FluentAssertions` (v8+) — commercial licences.
> * **.NET 11 ships this month.** Any package whose version tracks .NET (`Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`, `Microsoft.Extensions.*`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`) must be added with `--version "10.*"`, otherwise NuGet may hand you an `11.x` build that cannot run on `net10.0`.
> * Everything else in this guide (Npgsql, YARP, Serilog, OpenTelemetry, FluentValidation, StackExchange.Redis, xUnit, Testcontainers) is free to use.

---

# PART B — PHASE 0: FOUNDATION (Weeks 1–2)

**Phase 0 goal (from your roadmap):** *monorepo skeleton, shared BuildingBlocks, docker-compose with infrastructure, CI green on an empty solution, YARP gateway routing to two empty services — "the spine exists on day 1."*

**Why a whole phase before any feature?** Every later step assumes: Git works, Docker runs your database, your services log in a consistent format, errors look the same everywhere, and CI catches mistakes. Skipping this to "get to the fun part" is the number-one reason solo microservice projects collapse around week 6. The architecture document calls this *"pipeline before code"*. It is right.

**Phase 0 deliverable:** one command starts Postgres + Redis + RabbitMQ; one command starts a gateway and two empty services; `curl http://localhost:5000/api/v1/identity/ping` returns JSON *through the gateway*; every push runs a CI build.

---

## 0.1 Hardware & accounts

**Minimum:** 8 GB RAM (you will need to close the browser tabs), 4 cores, 30 GB free SSD. **Recommended:** 16 GB RAM, 6+ cores. Docker + 7 .NET services + Android emulator + IDE is heavy.

**Accounts to create now (free):**
* **GitHub** (code + CI). Enable two-factor authentication immediately — you are about to store a marketplace's source code there.
* **Docker Hub** (optional; only needed if you hit pull rate limits).
* A **Google Play Console / Apple Developer** account is *not* needed now, but the architecture doc warns verification takes weeks — start in Phase 6.
* **PayHere merchant account** (sandbox): the doc says start onboarding in Phase 1 because business-registration approval is slow. Do the *registration paperwork* now; the *integration* is Phase 2+.

---

## 0.2 Install the tools

🎯 **Goal:** every tool installed and verified with a version command.

| Tool | Version | Why you need it | Verify with |
|---|---|---|---|
| **Git** | latest | Version control | `git --version` |
| **.NET SDK** | **10.x (LTS)** | Backend language/runtime — download from `https://dotnet.microsoft.com/download` | `dotnet --version` → prints `10.0.x` |
| **Docker** | Docker Desktop (Win/Mac) or Docker Engine + Compose plugin (Linux) | Runs Postgres/Redis/RabbitMQ and later your services | `docker --version` and `docker compose version` |
| **Node.js** | current **LTS** | React toolchain | `node --version` and `npm --version` |
| **Flutter SDK** (stable) + **Android Studio** (SDK + emulator) | stable | Mobile apps | `flutter doctor` (fix every red ✗ it lists) |
| **Python** | 3.12+ | Recommender service, seed scripts | `python --version` (Windows may be `py --version`) |
| **IDE** | VS Code (+ *C# Dev Kit*, *Docker*, *ESLint*, *Prettier*, *Dart*, *Flutter*, *Python*, *EditorConfig*) **or** Visual Studio 2026 **or** JetBrains Rider | You write code here | open it |
| **DB client** | DBeaver (or pgAdmin — we also run it in Docker) | Look inside Postgres | open it |
| **API client** | **Bruno** (free, stores collections as files in your repo) — or Postman/Insomnia | Call your APIs | open it |

Then install the EF Core CLI tool (used for database migrations):

```bash
dotnet tool install --global dotnet-ef
dotnet ef --version
```

🧠 **Why .NET 10 specifically?** See Part A #1. Also, `dotnet --version` printing `8.x` or `9.x` means you installed the wrong one — uninstall/upgrade now, not later.

⚠️ **Windows notes**
* Docker Desktop requires **WSL 2** — enable it when the installer asks, restart, then start Docker Desktop and wait for the whale icon to say *running*.
* Use **PowerShell** or **Git Bash**. Commands in this guide are given for bash; PowerShell equivalents are shown where they differ.
* Line endings: we add a `.gitattributes` in 0.3 so Windows/Linux/Mac don't fight over CRLF vs LF.

⚠️ **macOS (Apple Silicon):** all images we use have `arm64` builds. If a container refuses to start with *"exec format error"*, you pulled the wrong architecture; add `platform: linux/arm64` or re-pull.

✅ **Check:** run all the verify commands above. Take a screenshot or paste the outputs into a file `docs/environment.md` — future-you will thank you when a teammate (or a fresh laptop) needs the same setup.

---

## 0.3 Git & GitHub

🎯 **Goal:** an empty private GitHub repo `lankamart`, cloned locally, with sensible ignore rules and a branching convention.

🧠 **Why monorepo (one repo for everything)?** Your 12 services share contracts (event definitions) and change together constantly. With separate repos you'd publish and version shared libraries on every tiny change — a solo developer gains nothing from that pain. Boundaries are enforced by *discipline and build gates*, not by repository walls (architecture doc §7.1).

🛠️ **Do**

```bash
# 1. Tell git who you are (once per machine)
git config --global user.name  "Your Name"
git config --global user.email "you@example.com"
git config --global init.defaultBranch main

# 2. Create the repo on github.com (private, no README), then:
git clone https://github.com/<your-username>/lankamart.git
cd lankamart
```

Create **`.gitattributes`** (normalises line endings — prevents the infamous "whole file changed" diffs):

```gitattributes
* text=auto eol=lf
*.sh text eol=lf
*.bat text eol=crlf
*.png binary
*.jpg binary
```

Create **`.gitignore`** (essentials — extend as needed):

```gitignore
# .NET
bin/
obj/
*.user
*.suo
.vs/
TestResults/
*.log

# Secrets & local config — NEVER commit these
.env
*.pem
*.pfx
keys/
appsettings.Production.json
appsettings.*.local.json

# IDEs
.idea/
.vscode/*
!.vscode/launch.json
!.vscode/tasks.json
!.vscode/extensions.json

# Node / React
node_modules/
dist/
.vite/
coverage/

# Flutter
.dart_tool/
build/
.flutter-plugins*
*.iml

# Python
__pycache__/
.venv/
*.pyc
.pytest_cache/

# Docker volumes created locally
infra/data/
```

**Branching convention (trunk-based, solo-friendly):**
* `main` is always green (CI passes) and always runnable.
* Work on short branches: `feat/identity-login`, `fix/cart-merge`, `chore/ci-cache`. Open a Pull Request **to yourself**, let CI run, then *squash-merge*. This is the habit employers look for in a portfolio repo.
* **Conventional commits** — `type(scope): message`:
  `feat(identity): add refresh token rotation` · `fix(orders): prevent double payment event` · `chore(ci): cache nuget` · `docs: add ADR-004` · `test(inventory): reservation expiry`.
* Tag finished phases: `git tag phase-0-complete && git push --tags`.

On GitHub: *Settings → Branches → Add rule for `main` → Require a pull request, Require status checks (after CI exists in 0.10)*.

✅ **Check:** `git status` shows a clean tree; `.gitignore` and `.gitattributes` are committed (`git commit -m "chore: add git config files" && git push`).

⚠️ **Pitfall:** committing a password once means it lives in Git history forever, even if you delete it later. If it happens, **rotate the secret** (change the password) — deleting the file is not enough.

---

## 0.4 Monorepo skeleton

🎯 **Goal:** the folder layout from architecture doc §7.1, adapted slightly (we add `src/ml` for Python).

```
lankamart/
├── .github/workflows/          CI definitions
├── .vscode/                    shared editor tasks (optional)
├── docs/                       architecture, ADRs, runbooks, API notes
│   └── adr/                    Architecture Decision Records
├── infra/
│   └── postgres/init/          SQL that runs when the DB container is first created
├── scripts/                    dev runner, demo-data seeder
├── src/
│   ├── shared/
│   │   ├── LankaMart.BuildingBlocks/   shared toolbox (logging, auth, errors…)
│   │   └── LankaMart.Contracts/        event definitions every service shares
│   ├── gateway/LankaMart.Gateway/      YARP API gateway
│   ├── services/
│   │   ├── identity/   users/   catalog/   inventory/   orders/   payments/
│   ├── ml/recommender/                 Python (FastAPI) algorithms service
│   └── clients/
│       ├── storefront-web/             React (Vite + TypeScript)
│       └── customer-app/               Flutter
├── tests/
├── docker-compose.yml
├── .env.example
├── global.json  Directory.Build.props  .editorconfig  LankaMart.slnx
└── README.md
```

🛠️ **Do**

```bash
# bash / Git Bash / macOS / Linux
mkdir -p .github/workflows docs/adr infra/postgres/init scripts tests \
         src/shared src/gateway src/services src/ml src/clients
```

```powershell
# PowerShell
"/.github/workflows","docs/adr","infra/postgres/init","scripts","tests","src/shared","src/gateway","src/services","src/ml","src/clients" | % { New-Item -ItemType Directory -Force -Path $_ | Out-Null }
```

✅ **Check:** `ls` shows the folders. (Git doesn't track empty folders; they'll appear in commits as you fill them.)

---

## 0.5 The .NET solution

🎯 **Goal:** one solution containing the two shared libraries, the gateway, two empty services, and one test project — building successfully.

🧠 **Why one solution?** `dotnet build` at the repo root builds everything; CI does the same; the IDE sees all projects and lets you refactor across them.

### 0.5.1 Pin the SDK and share build settings

**`global.json`** (guarantees everyone — and CI — uses .NET 10):

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

**`Directory.Build.props`** (applies to *every* project automatically — saves you repeating settings 15 times):

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <InvariantGlobalization>false</InvariantGlobalization>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <!-- CI passes -p:TreatWarningsAsErrors=true ; locally warnings don't block you -->
  </PropertyGroup>
</Project>
```

🧠 **Why `Nullable=enable`?** The compiler warns when something that *might be null* is used without a check. For a solo developer this catches an entire category of 2 a.m. production bugs at compile time (architecture doc §2).

**`.editorconfig`** (code style, enforced by the IDE):

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = space
indent_size = 4

[*.{json,yml,yaml,md,ts,tsx,js,jsx,css,html,csproj,props,xml}]
indent_size = 2

[*.cs]
csharp_style_namespace_declarations = file_scoped:warning
csharp_prefer_braces = true:suggestion
dotnet_style_qualification_for_field = false:suggestion
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
dotnet_naming_rule.private_fields_underscore.severity = suggestion
```

### 0.5.2 Create the projects

```bash
dotnet new sln -n LankaMart          # .NET 10 creates LankaMart.slnx (new XML solution format). For the old format add: --format sln

# shared libraries
dotnet new classlib -n LankaMart.BuildingBlocks -o src/shared/LankaMart.BuildingBlocks
dotnet new classlib -n LankaMart.Contracts      -o src/shared/LankaMart.Contracts

# empty web projects ("web" = the smallest ASP.NET Core template)
dotnet new web -n LankaMart.Gateway      -o src/gateway/LankaMart.Gateway
dotnet new web -n LankaMart.Identity.Api -o src/services/identity/LankaMart.Identity.Api
dotnet new web -n LankaMart.Users.Api    -o src/services/users/LankaMart.Users.Api
dotnet new web -n LankaMart.Catalog.Api  -o src/services/catalog/LankaMart.Catalog.Api
dotnet new web -n LankaMart.Inventory.Api -o src/services/inventory/LankaMart.Inventory.Api
dotnet new web -n LankaMart.Orders.Api   -o src/services/orders/LankaMart.Orders.Api
dotnet new web -n LankaMart.Payments.Api -o src/services/payments/LankaMart.Payments.Api

# tests (xUnit)
dotnet new xunit -n LankaMart.BuildingBlocks.Tests -o tests/LankaMart.BuildingBlocks.Tests
```

Delete the lines `<TargetFramework>`, `<Nullable>`, `<ImplicitUsings>` from each generated `.csproj` (they now come from `Directory.Build.props`). Delete the `Class1.cs` files in the class libraries.

Add everything to the solution:

```bash
# bash
dotnet sln add $(find src tests -name "*.csproj")
```
```powershell
# PowerShell
dotnet sln add (Get-ChildItem -Recurse -Filter *.csproj -Path src,tests | ForEach-Object FullName)
```

Wire the project references (who depends on whom):

```bash
# every service + gateway uses the shared libs
for p in src/gateway/LankaMart.Gateway src/services/identity/LankaMart.Identity.Api src/services/users/LankaMart.Users.Api \
         src/services/catalog/LankaMart.Catalog.Api src/services/inventory/LankaMart.Inventory.Api \
         src/services/orders/LankaMart.Orders.Api src/services/payments/LankaMart.Payments.Api; do
  dotnet add $p reference src/shared/LankaMart.BuildingBlocks src/shared/LankaMart.Contracts
done
dotnet add tests/LankaMart.BuildingBlocks.Tests reference src/shared/LankaMart.BuildingBlocks
```
*(PowerShell: run `dotnet add <proj> reference <lib1> <lib2>` once per project, or write the same loop with `foreach ($p in ...)`.)*

✅ **Check:** `dotnet build` at the repo root ends with **Build succeeded. 0 Error(s)**.

⚠️ **Pitfall:** if you see *"SDK 10.0.100 not found"*, your installed SDK is older/different. Run `dotnet --list-sdks`; adjust `global.json`'s `version` to the lowest 10.0.x you have installed.

*(Optional improvement for Phase 2: Central Package Management with `Directory.Packages.props`. We skip it now because test-project templates ship with pinned versions and trigger error NU1008 until you move them — a distraction at this stage.)*

---

## 0.6 Infrastructure with Docker Compose

🎯 **Goal:** `docker compose up -d` starts PostgreSQL (with per-service schemas and logins), Redis, RabbitMQ, pgAdmin — all healthy.

🧠 **Why Docker for infrastructure?** Installing Postgres/Redis/RabbitMQ directly on your laptop pollutes it, differs from production, and is impossible to reset cleanly. Containers are disposable: `docker compose down -v` = factory reset. It is also your **#1 skill goal**: you'll write Dockerfiles in 0.9.

**What each piece does for LankaMart:**

| Component | Role in the system | Used from |
|---|---|---|
| **PostgreSQL 17** | System of record for every service (one DB, one schema + one login per service in Phase 1) | Phase 1 |
| **Redis 7** | Hot stock counters + reservations, carts, OTP codes, idempotency keys, token blacklist, rate-limit counters | Phase 1 |
| **RabbitMQ 4 (management)** | Event bus: `user.registered`, `order.paid`, `payment.completed` … | Phase 1 |
| **pgAdmin** | Browser UI to inspect Postgres | Phase 1 (optional) |
| **Jaeger** *(profile `obs`)* | Visualises distributed traces — "one checkout = one trace across 5 services" | Phase 1 (recommended) |
| **Meilisearch** | Multilingual search engine | **Phase 3** — not started yet |
| **Object storage (S3-compatible)** | Images/KYC docs | **Phase 3** — not started yet (see Part A #5) |

### 0.6.1 `.env.example` (copy to `.env`, which is gitignored)

```env
# ---- Postgres ----
POSTGRES_USER=postgres
POSTGRES_PASSWORD=postgres_dev_pw
POSTGRES_DB=lankamart

# ---- RabbitMQ ----
RABBITMQ_USER=lankamart
RABBITMQ_PASSWORD=lankamart_dev_pw

# ---- pgAdmin ----
PGADMIN_EMAIL=admin@lankamart.local
PGADMIN_PASSWORD=admin_dev_pw
```

```bash
cp .env.example .env        # PowerShell: Copy-Item .env.example .env
```

### 0.6.2 `docker-compose.yml`

```yaml
name: lankamart

services:
  postgres:
    image: postgres:17-alpine          # 17 is used deliberately: stable data-directory layout for volume mounts
    container_name: lm-postgres
    restart: unless-stopped
    environment:
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: ${POSTGRES_DB}
    ports: ["5432:5432"]
    volumes:
      - pgdata:/var/lib/postgresql/data
      - ./infra/postgres/init:/docker-entrypoint-initdb.d:ro   # runs *.sql once, on first start only
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER} -d ${POSTGRES_DB}"]
      interval: 5s
      timeout: 3s
      retries: 20

  redis:
    image: redis:7-alpine
    container_name: lm-redis
    restart: unless-stopped
    command: ["redis-server", "--appendonly", "yes"]   # persistence: carts/idempotency survive restarts
    ports: ["6379:6379"]
    volumes: [redisdata:/data]
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 20

  rabbitmq:
    image: rabbitmq:4-management-alpine
    container_name: lm-rabbitmq
    restart: unless-stopped
    environment:
      RABBITMQ_DEFAULT_USER: ${RABBITMQ_USER}
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD}
    ports:
      - "5672:5672"      # AMQP — your services connect here
      - "15672:15672"    # management UI — http://localhost:15672
    volumes: [rabbitdata:/var/lib/rabbitmq]
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 10s
      timeout: 5s
      retries: 20

  pgadmin:
    image: dpage/pgadmin4:latest
    container_name: lm-pgadmin
    restart: unless-stopped
    environment:
      PGADMIN_DEFAULT_EMAIL: ${PGADMIN_EMAIL}
      PGADMIN_DEFAULT_PASSWORD: ${PGADMIN_PASSWORD}
    ports: ["5050:80"]
    depends_on:
      postgres: { condition: service_healthy }
    profiles: ["tools"]

  jaeger:
    image: jaegertracing/jaeger:latest        # dev only; pin a version before staging
    container_name: lm-jaeger
    restart: unless-stopped
    ports:
      - "16686:16686"   # UI — http://localhost:16686
      - "4317:4317"     # OTLP gRPC  (your services send traces here)
      - "4318:4318"     # OTLP HTTP
    profiles: ["obs"]

volumes:
  pgdata:
  redisdata:
  rabbitdata:
```

### 0.6.3 `infra/postgres/init/01-init.sql` — schemas and one login per service

🧠 **Why per-service logins?** Principle #1: *"No service reads another service's DB. Ever."* If every service connected as `postgres`, that rule would live only in your head. With a login that has rights on **only its own schema**, a bug (or a hacker) in the Catalog service physically cannot `SELECT * FROM orders.orders`. It also means that in Phase 2, "splitting databases" is just moving a schema to another server and changing a connection string.

```sql
-- Runs once, against the POSTGRES_DB database, when the data volume is empty.
-- DEV-ONLY passwords. Production gets real secrets from Vault/K8s Secrets (Phase 6).

CREATE EXTENSION IF NOT EXISTS pg_trgm;   -- trigram index: fast ILIKE '%text%' search (Catalog)

DO $$
DECLARE
  svc text;
BEGIN
  FOREACH svc IN ARRAY ARRAY['identity','users','catalog','inventory','orders','payments']
  LOOP
    EXECUTE format('CREATE ROLE %I_svc LOGIN PASSWORD %L', svc, svc || '_dev_pw');
    EXECUTE format('CREATE SCHEMA %I AUTHORIZATION %I_svc', svc, svc);
    -- each service sees its own schema first, then public (where pg_trgm lives)
    EXECUTE format('ALTER ROLE %I_svc SET search_path = %I, public', svc, svc);
    -- lets EF Core run "CREATE SCHEMA IF NOT EXISTS" during migrations (dev convenience)
    EXECUTE format('GRANT CREATE ON DATABASE %I TO %I_svc', current_database(), svc);
  END LOOP;
END $$;
```

Resulting logins: `identity_svc / identity_dev_pw`, `users_svc / users_dev_pw`, `catalog_svc / catalog_dev_pw`, `inventory_svc / inventory_dev_pw`, `orders_svc / orders_dev_pw`, `payments_svc / payments_dev_pw`.

### 0.6.4 Start and verify

```bash
docker compose up -d                       # infra only
docker compose --profile tools --profile obs up -d     # + pgAdmin + Jaeger (optional)
docker compose ps                          # all should say "healthy"
```

✅ **Check**
* `docker compose ps` → postgres, redis, rabbitmq all **healthy**.
* RabbitMQ UI `http://localhost:15672` → login `lankamart / lankamart_dev_pw`.
* Postgres: `docker exec -it lm-postgres psql -U postgres -d lankamart -c "\dn"` → lists schemas `identity, users, catalog, inventory, orders, payments`.
* Prove isolation: `docker exec -it lm-postgres psql -U catalog_svc -d lankamart -c "SELECT * FROM orders.anything;"` → **permission denied for schema orders**. That error message is the *success* here.
* Redis: `docker exec -it lm-redis redis-cli ping` → `PONG`.

⚠️ **Pitfalls**
* The init script runs **only the first time** the `pgdata` volume is created. If you edit it later, you must reset: `docker compose down -v` (⚠️ deletes all DB data) then `up -d`.
* *"port is already allocated"* → another Postgres/Redis is running locally. Stop it, or change the left side of `"5432:5432"` (e.g., `"5433:5432"`) **and** your connection strings.
* Docker Desktop not running → `docker compose` errors with *"cannot connect to the Docker daemon"*. Start it.

**Handy commands:** `docker compose logs -f postgres` (live logs) · `docker compose stop` (pause, keep data) · `docker compose down` (remove containers, keep data) · `docker compose down -v` (remove containers **and data**).

---

## 0.7 BuildingBlocks — the shared toolbox

🎯 **Goal:** one class library that gives every service the same logging, tracing, error format, health checks, authentication, messaging setup, validation, and money type — so each service's `Program.cs` stays ~30 lines.

🧠 **Why now?** Consistency is the cheapest feature you will ever build. If every service reports errors differently, your React/Flutter code needs 7 error parsers. If every log line has a `trace_id`, you can find one customer's failed checkout across 5 services in seconds (architecture doc §5.5). Build it once, reuse it everywhere.

**What goes in BuildingBlocks (and what must NOT):**
* ✅ Cross-cutting plumbing: logging, telemetry, errors, auth wiring, messaging wiring, tiny value types (`Money`).
* ❌ Business logic. BuildingBlocks knows nothing about "orders" or "products". If you're tempted to put a business rule here, it belongs in a service.

### 0.7.1 Project file and packages

Replace `src/shared/LankaMart.BuildingBlocks/LankaMart.BuildingBlocks.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <!-- lets a class library use ASP.NET Core types (WebApplication, HttpContext, ...) -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
```

> ⚠️ **Version rule (important this week).** .NET 11 ships in November 2026, so NuGet may soon offer `11.x` of Microsoft packages that **cannot be used from a `net10.0` project**. Therefore: any package whose version tracks .NET — `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`, `Microsoft.Extensions.*`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions` — must be added with `--version "10.*"`. Third-party packages (Serilog, OpenTelemetry, FluentValidation, StackExchange.Redis…) don't need it. MassTransit needs `--version "8.*"` (Part A).

```bash
cd src/shared/LankaMart.BuildingBlocks

# logging
dotnet add package Serilog.AspNetCore
# telemetry
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package OpenTelemetry.Instrumentation.Runtime
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package Npgsql.OpenTelemetry
# auth + API docs
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version "10.*"
dotnet add package Microsoft.AspNetCore.OpenApi --version "10.*"
dotnet add package Scalar.AspNetCore
# validation, redis, EF helper, health checks, resilience
dotnet add package FluentValidation
dotnet add package FluentValidation.DependencyInjectionExtensions
dotnet add package StackExchange.Redis
dotnet add package Microsoft.EntityFrameworkCore.Relational --version "10.*"
dotnet add package AspNetCore.HealthChecks.NpgSql
dotnet add package AspNetCore.HealthChecks.Redis
dotnet add package Microsoft.Extensions.Http.Resilience --version "10.*"
# messaging — MUST be v8 (v9 is commercial)
dotnet add package MassTransit.RabbitMQ --version "8.*"
cd ../../..
```

### 0.7.2 The code

Create these files under `src/shared/LankaMart.BuildingBlocks/`. Use **one namespace**, `LankaMart.BuildingBlocks`, everywhere — it keeps `using` lines trivial.

**`Errors/AppExceptions.cs`** — business errors that map to HTTP status codes

```csharp
namespace LankaMart.BuildingBlocks;

/// <summary>Base class for errors we *expect* (validation, not-found, conflicts). Anything else becomes HTTP 500.</summary>
public abstract class AppException(string code, string message, int status) : Exception(message)
{
    public string Code { get; } = code;      // machine-readable, e.g. "OUT_OF_STOCK" — clients switch on this
    public int Status { get; } = status;     // HTTP status
}

public sealed class NotFoundException(string message = "Resource not found") : AppException("NOT_FOUND", message, 404);
public sealed class UnauthorizedException(string code = "UNAUTHORIZED", string message = "Authentication required") : AppException(code, message, 401);
public sealed class ForbiddenException(string code = "FORBIDDEN", string message = "You are not allowed to do this") : AppException(code, message, 403);
public sealed class ConflictException(string code, string message) : AppException(code, message, 409);
public sealed class BusinessRuleException(string code, string message) : AppException(code, message, 422);
public sealed class TooManyRequestsException(string code, string message) : AppException(code, message, 429);
public sealed class ServiceUnavailableException(string code, string message) : AppException(code, message, 503);
```

**`Errors/GlobalExceptionHandler.cs`** — one error format everywhere (RFC 7807 "Problem Details")

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LankaMart.BuildingBlocks;

public sealed class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, code, title) = ex switch
        {
            AppException a => (a.Status, a.Code, a.Message),
            BadHttpRequestException b => (400, "BAD_REQUEST", b.Message),
            _ => (500, "INTERNAL_ERROR", "An unexpected error occurred.")   // NEVER leak ex.Message for unknown errors
        };

        if (status >= 500) log.LogError(ex, "Unhandled exception");
        else log.LogInformation("Handled {Code}: {Message}", code, ex.Message);

        ctx.Response.StatusCode = status;
        var pd = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://lankamart.dev/errors/{code.ToLowerInvariant().Replace('_', '-')}"
        };
        pd.Extensions["code"] = code;
        pd.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;

        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = pd, Exception = ex });
    }
}
```

🧠 *Why never leak unknown exception messages?* A raw exception can contain SQL, file paths or connection strings — an information-disclosure vulnerability. The client gets a generic message + a `traceId`; **you** look up the real error in logs using that trace id.

**`Errors/Validation.cs`** — FluentValidation wired into minimal APIs

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LankaMart.BuildingBlocks;

public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var arg = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (arg is null) return Results.Problem(statusCode: 400, title: "Request body is required", extensions: new Dictionary<string, object?> { ["code"] = "BODY_REQUIRED" });

        var validator = ctx.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is not null)
        {
            var result = await validator.ValidateAsync(arg, ctx.HttpContext.RequestAborted);
            if (!result.IsValid)
                return Results.ValidationProblem(result.ToDictionary(), extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
        }
        return await next(ctx);
    }
}

public static class ValidationExtensions
{
    /// <summary>Validate the request body of type T before the handler runs.</summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder b) where T : class
        => b.AddEndpointFilter<ValidationFilter<T>>();
}
```

**`Auth/Roles.cs`**, **`Auth/AuthExtensions.cs`** — roles, JWT validation, fail-closed authorization

```csharp
// Roles.cs
namespace LankaMart.BuildingBlocks;

/// <summary>The 9 actors from System Scenarios Part 1 (+ a reserved finance role, so Phase 5 needs no schema change).</summary>
public static class Roles
{
    public const string Customer = "customer";
    public const string Seller = "seller";
    public const string Rider = "rider";
    public const string SellerManager = "seller_manager";
    public const string CustomerManager = "customer_manager";
    public const string RiderManager = "rider_manager";
    public const string AuditManager = "audit_manager";   // read-only everywhere (enforced in Phase 6)
    public const string SuperAdmin = "super_admin";
    public const string FinanceManager = "finance_manager"; // reserved for Phase 5

    public static readonly string[] All =
        [Customer, Seller, Rider, SellerManager, CustomerManager, RiderManager, AuditManager, SuperAdmin, FinanceManager];
}

public static class Policies
{
    public const string Customer = nameof(Customer);
    public const string Seller = nameof(Seller);
    public const string SellerAdmin = nameof(SellerAdmin);   // seller_manager OR super_admin
    public const string SuperAdmin = nameof(SuperAdmin);
}
```

```csharp
// AuthExtensions.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LankaMart.BuildingBlocks;

public sealed class AuthOptions
{
    public string Authority { get; set; } = "";                 // Identity service base URL (where the public keys live)
    public string Issuer { get; set; } = "lankamart-identity";  // must equal the 'iss' claim in tokens
    public string Audience { get; set; } = "lankamart-api";     // must equal the 'aud' claim
    public bool RequireHttps { get; set; } = true;              // false ONLY in local dev
}

public static class AuthExtensions
{
    public static IServiceCollection AddPlatformAuth(this IServiceCollection services, IConfiguration config)
    {
        var opt = config.GetSection("Auth").Get<AuthOptions>() ?? throw new InvalidOperationException("Missing 'Auth' configuration section.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.Authority = opt.Authority;                 // JwtBearer downloads public keys from {Authority}/.well-known/openid-configuration
            o.RequireHttpsMetadata = opt.RequireHttps;
            o.MapInboundClaims = false;                  // keep claim names as-is: "sub", "role" (not long Microsoft URIs)
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = opt.Issuer,
                ValidateAudience = true, ValidAudience = opt.Audience,
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],   // blocks "alg:none" / algorithm-confusion attacks
                NameClaimType = "sub", RoleClaimType = "role"
            };
        });

        services.AddAuthorizationBuilder()
            // FAIL CLOSED (Principle #6): every endpoint requires a valid token unless it explicitly says .AllowAnonymous()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Customer, p => p.RequireRole(Roles.Customer))
            .AddPolicy(Policies.Seller, p => p.RequireRole(Roles.Seller))
            .AddPolicy(Policies.SellerAdmin, p => p.RequireRole(Roles.SellerManager, Roles.SuperAdmin))
            .AddPolicy(Policies.SuperAdmin, p => p.RequireRole(Roles.SuperAdmin));

        return services;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirstValue("sub"), out var id) ? id : throw new UnauthorizedException();

    public static Guid? TryUserId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirstValue("sub"), out var id) ? id : null;

    public static Guid SellerId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirstValue("seller_id"), out var id) ? id : throw new ForbiddenException("NOT_A_SELLER", "Seller account required");

    public static bool HasFlag(this ClaimsPrincipal u, string claim) =>
        bool.TryParse(u.FindFirstValue(claim), out var v) && v;

    /// <summary>Phase-1 rule from System Scenarios §2.1: email AND phone must be verified before ordering.</summary>
    public static bool IsFullyVerified(this ClaimsPrincipal u) => u.HasFlag("email_verified") && u.HasFlag("phone_verified");
}
```

🧠 *Why `ValidAlgorithms = [RS256]`?* The classic JWT attack sends a token whose header says `"alg":"none"` (no signature) or swaps RS256→HS256 using the *public* key as the HMAC secret. Pinning the allowed algorithm kills both. It's on your pen-test list (§6.3 of the architecture doc); now you've defended before attacking.

**`Http/InternalApi.cs`** — service-to-service calls (`/internal/*`)

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LankaMart.BuildingBlocks;

/// <summary>Rejects calls that don't carry the shared internal key. Internal routes are NEVER routed by the gateway.</summary>
public sealed class InternalOnlyFilter(IConfiguration cfg) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var expected = cfg["Internal:ApiKey"];
        var given = ctx.HttpContext.Request.Headers["X-Internal-Key"].ToString();
        if (string.IsNullOrEmpty(expected) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
            return Results.Problem(statusCode: 401, title: "Invalid internal key", extensions: new Dictionary<string, object?> { ["code"] = "INTERNAL_AUTH_FAILED" });
        return await next(ctx);
    }
}

public sealed class InternalKeyHandler(IConfiguration cfg) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.TryAddWithoutValidation("X-Internal-Key", cfg["Internal:ApiKey"]);
        return base.SendAsync(request, ct);
    }
}

public static class InternalApiExtensions
{
    /// <summary>app.MapInternalGroup("/api/v1/internal/inventory") → anonymous for JWT, but protected by the internal key.</summary>
    public static RouteGroupBuilder MapInternalGroup(this IEndpointRouteBuilder app, string prefix) =>
        app.MapGroup(prefix).AllowAnonymous().AddEndpointFilter<InternalOnlyFilter>();

    /// <summary>Typed HttpClient for calling another service: base URL from "Services:{name}", internal key, retries, circuit breaker, timeouts.</summary>
    public static IHttpClientBuilder AddInternalClient<TClient, TImpl>(this IServiceCollection s, IConfiguration c, string serviceName)
        where TClient : class where TImpl : class, TClient
    {
        s.AddTransient<InternalKeyHandler>();
        return s.AddHttpClient<TClient, TImpl>(http =>
                http.BaseAddress = new Uri(c[$"Services:{serviceName}"] ?? throw new InvalidOperationException($"Missing 'Services:{serviceName}' config")))
            .AddHttpMessageHandler<InternalKeyHandler>()
            .AddStandardResilienceHandler();   // Polly v8: retry (exp. backoff + jitter) + circuit breaker + timeouts
    }
}
```

🧠 *Why a circuit breaker?* If Payment is down and Orders keeps calling it, every checkout request waits for a timeout, threads pile up, and Orders dies too. A circuit breaker "opens" after repeated failures and fails instantly for a while — a dying dependency can't take down its caller (architecture doc §5.3).

**`Messaging/MessagingExtensions.cs`** — RabbitMQ via MassTransit

```csharp
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LankaMart.BuildingBlocks;

public static class MessagingExtensions
{
    /// <param name="serviceName">Prefix for queue names, e.g. "catalog" → queue "catalog-seller-approved". Prevents two services sharing a queue by accident.</param>
    /// <param name="configure">Register your consumers here: x => x.AddConsumers(typeof(Program).Assembly)</param>
    public static IServiceCollection AddPlatformMessaging(this IServiceCollection services, IConfiguration config,
        string serviceName, Action<IBusRegistrationConfigurator>? configure = null)
    {
        services.AddMassTransit(x =>
        {
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, includeNamespace: false));
            configure?.Invoke(x);

            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(config["RabbitMq:Host"] ?? "localhost", ushort.Parse(config["RabbitMq:Port"] ?? "5672"), "/", h =>
                {
                    h.Username(config["RabbitMq:Username"] ?? "lankamart");
                    h.Password(config["RabbitMq:Password"] ?? "lankamart_dev_pw");
                });
                // RabbitMQ delivers AT-LEAST-ONCE → consumers must be idempotent (Principle #3). Retry transient failures,
                // then the message goes to the "<queue>_error" queue (dead-letter) where you can inspect/replay it.
                cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
                cfg.ConfigureEndpoints(ctx);
            });
        });
        return services;
    }
}
```

**`Platform/PlatformExtensions.cs`** — logging, tracing, health, OpenAPI, Redis, Postgres checks

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using StackExchange.Redis;

namespace LankaMart.BuildingBlocks;

public sealed class TraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory pf)
    {
        if (Activity.Current is { } a)
            logEvent.AddPropertyIfAbsent(pf.CreateProperty("trace_id", a.TraceId.ToString()));
    }
}

public static class PlatformExtensions
{
    /// <summary>Call first in Program.cs: logging + tracing + error handling + health + OpenAPI.</summary>
    public static WebApplicationBuilder AddPlatformDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        // ---------- Logging: readable in dev, JSON in containers (Loki/ELK friendly) ----------
        builder.Services.AddSerilog((_, lc) =>
        {
            lc.MinimumLevel.Information()
              .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
              .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
              .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
              .ReadFrom.Configuration(builder.Configuration)      // lets appsettings override levels
              .Enrich.FromLogContext()
              .Enrich.WithProperty("service", serviceName)
              .Enrich.With<TraceEnricher>();

            if (builder.Environment.IsDevelopment())
                lc.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{service}] {Message:lj}{NewLine}{Exception}");
            else
                lc.WriteTo.Console(new RenderedCompactJsonFormatter());
        });

        // ---------- Tracing (OpenTelemetry) → Jaeger ----------
        var otlp = builder.Configuration["Otel:Endpoint"];   // e.g. http://localhost:4317 ; empty = tracing stays local
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                 .AddHttpClientInstrumentation()
                 .AddNpgsql()
                 .AddSource("MassTransit");
                if (!string.IsNullOrWhiteSpace(otlp)) t.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
            })
            .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation());

        // ---------- Errors, health, OpenAPI ----------
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddHealthChecks();
        builder.Services.AddOpenApi();
        return builder;
    }

    /// <summary>Call after Build(): exception handler, request logging, /health/*, OpenAPI + Scalar UI (dev only).</summary>
    public static WebApplication UsePlatformDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();      // uses GlobalExceptionHandler
        app.UseStatusCodePages();       // empty 401/403/404 → Problem Details JSON
        app.UseSerilogRequestLogging(); // one structured line per request

        // /health/live  = "is the process up?"        → Kubernetes LIVENESS probe (restart me if this fails)
        // /health/ready = "can I serve traffic now?"  → Kubernetes READINESS probe (stop routing to me if this fails)
        // Mixing them up is the classic cause of restart loops (architecture doc §7.4).
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();                  // /openapi/v1.json
            app.MapScalarApiReference().AllowAnonymous();       // /scalar/v1  (interactive API docs)
        }
        return app;
    }

    public static IServiceCollection AddPlatformPostgresHealth(this IServiceCollection s, IConfiguration c)
    {
        s.AddHealthChecks().AddNpgSql(c.GetConnectionString("Default")!, name: "postgres", tags: ["ready"]);
        return s;
    }

    public static IServiceCollection AddPlatformRedis(this IServiceCollection s, IConfiguration c)
    {
        var cs = c["Redis:ConnectionString"] ?? "localhost:6379";
        var opts = ConfigurationOptions.Parse(cs);
        opts.AbortOnConnectFail = false;       // don't crash at startup if Redis is still booting; it reconnects
        s.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(opts));
        s.AddHealthChecks().AddRedis(cs, name: "redis", tags: ["ready"]);
        return s;
    }
}
```

**`Data/MigrationExtensions.cs`** — apply EF migrations at startup (dev convenience)

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LankaMart.BuildingBlocks;

public static class MigrationExtensions
{
    /// <summary>Applies pending migrations, retrying while the DB container is still starting. In production use a separate migration job (Phase 2).</summary>
    public static async Task MigrateDatabaseAsync<TContext>(this WebApplication app) where TContext : DbContext
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<TContext>>();
        for (var attempt = 1; ; attempt++)
        {
            try { await db.Database.MigrateAsync(); log.LogInformation("Database is up to date"); return; }
            catch (Exception ex) when (attempt < 10)
            {
                log.LogWarning("DB migrate attempt {Attempt}/10 failed: {Message}. Retrying in 3s…", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
```

**`Domain/Money.cs`, `Domain/Paging.cs`, `Domain/PhoneNumber.cs`** — small value types used everywhere

```csharp
namespace LankaMart.BuildingBlocks;

/// <summary>Money as integer minor units (cents). NEVER float/double: 0.1 + 0.2 != 0.3 is how platforms lose real money.
/// LKR 1,250.50 is stored as 125050.</summary>
public readonly record struct Money(long Cents, string Currency = "LKR")
{
    public static Money Zero(string currency = "LKR") => new(0, currency);
    public static Money operator +(Money a, Money b) { Same(a, b); return new(checked(a.Cents + b.Cents), a.Currency); }
    public static Money operator -(Money a, Money b) { Same(a, b); return new(checked(a.Cents - b.Cents), a.Currency); }
    public static Money operator *(Money a, int qty) => new(checked(a.Cents * qty), a.Currency);   // checked = throw on overflow instead of wrapping silently
    private static void Same(Money a, Money b) { if (a.Currency != b.Currency) throw new InvalidOperationException("Currency mismatch"); }
    public override string ToString() => $"{Currency} {Cents / 100m:N2}";   // display only
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public static class Paging
{
    /// <summary>Caps page size (API4: unrestricted resource consumption — never trust ?page_size=100000).</summary>
    public static (int page, int size) Normalize(int? page, int? pageSize, int max = 50, int def = 20)
        => (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? def, 1, max));
}

public static class PhoneNumber
{
    /// <summary>Normalises Sri Lankan numbers to E.164: 0771234567 / 94771234567 / +94 77 123 4567 → +94771234567.</summary>
    public static bool TryNormalizeLk(string? input, out string e164)
    {
        e164 = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = new string(input.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (s.StartsWith("+94")) s = s[3..];
        else if (s.StartsWith("94") && s.Length == 11) s = s[2..];
        else if (s.StartsWith('0')) s = s[1..];
        else return false;
        if (s.Length != 9 || !s.All(char.IsDigit)) return false;
        e164 = "+94" + s;
        return true;
    }
}
```

### 0.7.3 Tests for the toolbox (your first green CI)

`tests/LankaMart.BuildingBlocks.Tests/MoneyTests.cs`:

```csharp
using LankaMart.BuildingBlocks;
using Xunit;

public class MoneyTests
{
    [Fact] public void Adds_without_losing_cents() =>
        Assert.Equal(new Money(125050), new Money(100000) + new Money(25050));

    [Fact] public void Multiplies_by_quantity() =>
        Assert.Equal(new Money(75000), new Money(25000) * 3);

    [Fact] public void Different_currencies_cannot_be_added() =>
        Assert.Throws<InvalidOperationException>(() => new Money(1, "LKR") + new Money(1, "USD"));

    [Fact] public void Overflow_throws_instead_of_wrapping() =>
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue) * 2);
}

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0771234567", "+94771234567")]
    [InlineData("+94 77 123 4567", "+94771234567")]
    [InlineData("94771234567", "+94771234567")]
    public void Normalises_valid_numbers(string input, string expected)
    {
        Assert.True(PhoneNumber.TryNormalizeLk(input, out var e164));
        Assert.Equal(expected, e164);
    }

    [Theory] [InlineData("")] [InlineData("12345")] [InlineData("+4477123456")] [InlineData("07712345678")]
    public void Rejects_invalid_numbers(string input) => Assert.False(PhoneNumber.TryNormalizeLk(input, out _));
}
```

✅ **Check:** `dotnet build` → succeeded; `dotnet test` → all tests pass (green). Commit: `feat(shared): add BuildingBlocks toolbox`.

⚠️ **Pitfalls**
* *"The type or namespace 'WebApplication' could not be found"* in BuildingBlocks → the `<FrameworkReference Include="Microsoft.AspNetCore.App" />` is missing.
* *NU1202 "package is not compatible with net10.0"* → you added a Microsoft package without `--version "10.*"`. Remove the line from the `.csproj` and re-add with the version.
* If `AddNpgsql()` isn't found → add `using OpenTelemetry.Trace;` (extension lives there) and confirm `Npgsql.OpenTelemetry` is referenced.

---

## 0.8 Empty services + the YARP gateway

🎯 **Goal:** the "spine". A request to the **gateway** is forwarded to the right service. Two empty services (Identity, Catalog) answer `/api/v1/ping`.

🧠 **Why a gateway at all?** Clients (React, Flutter) should know **one** address. The gateway is the single front door: it routes `/api/v1/orders/*` to the Order service, enforces rate limits, validates JWTs, handles CORS, and (critically) **does not expose** internal endpoints. Services behind it can move, scale, or be renamed without any client change. YARP is Microsoft's reverse-proxy library — you configure routes in JSON, no code.

### 0.8.1 Service `Program.cs` (identical shape for Identity and Catalog for now)

`src/services/identity/LankaMart.Identity.Api/Program.cs`:

```csharp
using LankaMart.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults("identity");

var app = builder.Build();
app.UsePlatformDefaults();

app.MapGet("/api/v1/ping", () => Results.Ok(new { service = "identity", status = "ok", time = DateTimeOffset.UtcNow }));

app.Run();
```

Do the same in Catalog with `"catalog"` as the name. Remove the template's `launchSettings` dependence by running with explicit URLs (see 0.11) or edit `Properties/launchSettings.json` and set `applicationUrl` to the port from **Appendix A** (Identity `http://localhost:5101`, Catalog `http://localhost:5103`).

### 0.8.2 Gateway

```bash
cd src/gateway/LankaMart.Gateway && dotnet add package Yarp.ReverseProxy && cd ../../..
```

`src/gateway/LankaMart.Gateway/Program.cs`:

```csharp
using LankaMart.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults("gateway");
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();
app.UsePlatformDefaults();
app.MapReverseProxy();
app.Run();
```

`src/gateway/LankaMart.Gateway/appsettings.json` (Phase 0 demo routes — replaced with real routes in §1.7):

```json
{
  "AllowedHosts": "*",
  "Otel": { "Endpoint": "" },
  "ReverseProxy": {
    "Routes": {
      "identity-ping": {
        "ClusterId": "identity",
        "Match": { "Path": "/api/v1/identity/{**rest}" },
        "Transforms": [ { "PathPattern": "/api/v1/{**rest}" } ]
      },
      "catalog-ping": {
        "ClusterId": "catalog",
        "Match": { "Path": "/api/v1/catalog/{**rest}" },
        "Transforms": [ { "PathPattern": "/api/v1/{**rest}" } ]
      }
    },
    "Clusters": {
      "identity": { "Destinations": { "d1": { "Address": "http://localhost:5101/" } } },
      "catalog":  { "Destinations": { "d1": { "Address": "http://localhost:5103/" } } }
    }
  }
}
```

🧠 *Read the route:* "a request whose path starts with `/api/v1/identity/` → send to cluster `identity`, but rewrite the path to `/api/v1/` + whatever followed." So `/api/v1/identity/ping` becomes `/api/v1/ping` at the Identity service.

### 0.8.3 Run and test

Open three terminals (or use the runner from 0.11):

```bash
dotnet run --project src/services/identity/LankaMart.Identity.Api --urls http://localhost:5101
dotnet run --project src/services/catalog/LankaMart.Catalog.Api   --urls http://localhost:5103
dotnet run --project src/gateway/LankaMart.Gateway                --urls http://localhost:5100
```

✅ **Check**
```bash
curl http://localhost:5101/api/v1/ping               # straight to the service
curl http://localhost:5100/api/v1/identity/ping      # through the gateway — SAME answer = the spine works
curl http://localhost:5100/api/v1/catalog/ping
curl -i http://localhost:5100/health/live            # 200 OK
curl -i http://localhost:5100/api/v1/nothing/here    # 404 as Problem Details JSON (not an HTML page, not a stack trace)
```
Commit: `feat(gateway): YARP spine with two empty services`.

⚠️ **Pitfalls**
* **502 Bad Gateway** from the gateway = the target service isn't running or the port in `Clusters` is wrong.
* On macOS, **port 5000 is taken by AirPlay Receiver**; that's why we use 5100+. Don't "simplify" the gateway back to 5000.
* `dotnet run` picks a launch profile that may override `--urls`. If your service ignores the port, add `--no-launch-profile`.

---

## 0.9 Learn Docker: containerise a service

🎯 **Goal:** build and run the gateway as a Docker image — and *understand why it initially fails* (container networking). Docker is your #1 skill goal; this exercise is worth more than any video.

🧠 **Concepts in 60 seconds**
* **Image** = frozen recipe + files (like a zip of a mini-Linux with your app). **Container** = a running instance of an image.
* **Multi-stage build** = stage 1 uses the big SDK image to *compile*; stage 2 copies only the compiled output into the small runtime image. Result: ~100–150 MB instead of 1 GB, and no compilers in production.
* **Non-root user** = if someone hacks your app, they are not `root` inside the container.

🛠️ **Do** — create `infra/docker/dotnet.Dockerfile` (one Dockerfile reused by every service):

```dockerfile
# syntax=docker/dockerfile:1

# ---------- Stage 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /repo
COPY global.json Directory.Build.props ./
COPY src ./src
ARG PROJECT            # e.g. src/gateway/LankaMart.Gateway/LankaMart.Gateway.csproj
RUN dotnet publish "$PROJECT" -c Release -o /out --nologo

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /out .
ARG DLL                # e.g. LankaMart.Gateway.dll
ENV APP_DLL=$DLL
USER $APP_UID          # built into Microsoft's images: a non-root user
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet $APP_DLL"]
```

Create `.dockerignore` in the repo root (keeps the build context small and secrets out of images):

```
**/bin
**/obj
**/node_modules
**/.git
**/.vs
**/.idea
.env
*.md
src/clients
src/ml
tests
```

Build and run:

```bash
docker build -f infra/docker/dotnet.Dockerfile \
  --build-arg PROJECT=src/gateway/LankaMart.Gateway/LankaMart.Gateway.csproj \
  --build-arg DLL=LankaMart.Gateway.dll \
  -t lankamart/gateway:dev .

docker images lankamart/gateway:dev        # look at the SIZE
docker run --rm -p 5100:8080 lankamart/gateway:dev
```

✅ **Check 1:** `curl http://localhost:5100/health/live` → 200. The container works.
✅ **Check 2 (the lesson):** `curl http://localhost:5100/api/v1/identity/ping` → **502**. Why? Inside the container, `localhost` means *the container itself*, not your laptop. The gateway looked for Identity at its own port 5101 and found nothing.

**Fix it the Docker way** — override the config with environment variables (`__` replaces `:` / nesting):

```bash
docker run --rm -p 5100:8080 \
  -e ReverseProxy__Clusters__identity__Destinations__d1__Address=http://host.docker.internal:5101/ \
  -e ReverseProxy__Clusters__catalog__Destinations__d1__Address=http://host.docker.internal:5103/ \
  lankamart/gateway:dev
# Linux only: also add   --add-host=host.docker.internal:host-gateway
```
Now the ping works. *This is exactly how Kubernetes will configure your services later: same image everywhere, behaviour set by environment variables.* (In §1.19 you'll replace `host.docker.internal` with Docker Compose service names, which is the proper solution.)

⚠️ **Pitfalls:** *"COPY failed: file not found"* → you ran `docker build` from a sub-folder; the build **context** (final `.`) must be the repo root. · Slow builds → normal the first time (it downloads the SDK image); later builds use the cache.

---

## 0.10 CI pipeline — "pipeline before code"

🎯 **Goal:** every push and pull request automatically builds and tests the solution on a clean machine.

🧠 **Why now, with almost no code?** CI is a *habit*, not a feature. A pipeline that has been green since day 1 tells you instantly which commit broke things. Adding it in week 20 means debugging 20 weeks of accumulated problems at once.

`.github/workflows/ci.yml`:

```yaml
name: ci

on:
  push:
    branches: [main]
  pull_request:

concurrency:            # cancel older runs of the same branch
  group: ci-${{ github.ref }}
  cancel-in-progress: true

jobs:
  dotnet:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Restore
        run: dotnet restore
      - name: Build (warnings are errors in CI)
        run: dotnet build --no-restore -c Release -p:TreatWarningsAsErrors=true
      - name: Test
        run: dotnet test --no-build -c Release --logger "trx;LogFileName=results.trx"
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: '**/results.trx'

  # Enabled automatically once the folders exist (Phase 1 steps 1.13 / 1.15)
  web:
    runs-on: ubuntu-latest
    if: ${{ hashFiles('src/clients/storefront-web/package.json') != '' }}
    defaults: { run: { working-directory: src/clients/storefront-web } }
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: lts/*, cache: npm, cache-dependency-path: src/clients/storefront-web/package-lock.json }
      - run: npm ci
      - run: npm run lint --if-present
      - run: npm run build

  python:
    runs-on: ubuntu-latest
    if: ${{ hashFiles('src/ml/recommender/requirements.txt') != '' }}
    defaults: { run: { working-directory: src/ml/recommender } }
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with: { python-version: '3.12' }
      - run: pip install -r requirements.txt
      - run: pytest -q
```

Then on GitHub: *Settings → Branches → `main` rule → Require status checks → select `dotnet`*.

✅ **Check:** push a branch, open a PR, watch the **Actions** tab turn green. Then deliberately break a test (`Assert.Equal(1, 2)`), push, watch it go red, and fix it. Seeing CI fail on purpose once removes its mystery.

⚠️ **Pitfall:** CI treats warnings as errors; locally you won't see them as errors. If CI fails with a warning you never noticed, run locally: `dotnet build -c Release -p:TreatWarningsAsErrors=true`.

---

## 0.11 Dev runner & ports

🎯 **Goal:** one command starts infra and every service; one fixed port per component.

**Port map** (full table in Appendix A):

| Component | Port | | Component | Port |
|---|---|---|---|---|
| Gateway | **5100** | | Orders | 5105 |
| Identity | 5101 | | Payments | 5106 |
| Users | 5102 | | Recommender (Python) | 5110 |
| Catalog | 5103 | | React dev server | 5173 |
| Inventory | 5104 | | Postgres / Redis / RabbitMQ | 5432 / 6379 / 5672 |

`scripts/dev.py` — starts Docker infra, then every service (works on Windows/Mac/Linux):

```python
#!/usr/bin/env python3
"""Start infra + .NET services.   python scripts/dev.py            (everything)
                                  python scripts/dev.py gateway identity   (just these)"""
import os, subprocess, sys, threading, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SERVICES = {   # name: (project folder, port)
    "gateway":   ("src/gateway/LankaMart.Gateway", 5100),
    "identity":  ("src/services/identity/LankaMart.Identity.Api", 5101),
    "users":     ("src/services/users/LankaMart.Users.Api", 5102),
    "catalog":   ("src/services/catalog/LankaMart.Catalog.Api", 5103),
    "inventory": ("src/services/inventory/LankaMart.Inventory.Api", 5104),
    "orders":    ("src/services/orders/LankaMart.Orders.Api", 5105),
    "payments":  ("src/services/payments/LankaMart.Payments.Api", 5106),
}

def pump(name, proc):
    for line in proc.stdout:
        print(f"[{name:<9}] {line}", end="")

def stop(p):
    if os.name == "nt":   # dotnet run spawns a child; kill the whole tree on Windows
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(p.pid)], capture_output=True)
    else:
        p.terminate()

def main():
    wanted = sys.argv[1:] or list(SERVICES)
    subprocess.run(["docker", "compose", "up", "-d"], cwd=ROOT, check=True)
    procs = []
    for name in wanted:
        folder, port = SERVICES[name]
        if not (ROOT / folder).exists():
            continue
        env = {**os.environ, "ASPNETCORE_ENVIRONMENT": "Development", "ASPNETCORE_URLS": f"http://localhost:{port}"}
        p = subprocess.Popen(["dotnet", "run", "--no-launch-profile", "--project", str(ROOT / folder)],
                             cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        threading.Thread(target=pump, args=(name, p), daemon=True).start()
        procs.append(p)
    try:
        for p in procs: p.wait()
    except KeyboardInterrupt:
        print("\nStopping…")
    finally:
        for p in procs: stop(p)

if __name__ == "__main__":
    main()
```

When you are *editing one service*, run it alone with hot reload instead: `dotnet watch run --project <folder> --no-launch-profile` (set `ASPNETCORE_URLS` yourself).

**Optional VS Code debugging:** later add `.vscode/launch.json` with a *compound* configuration listing each service so F5 starts them all under the debugger.

✅ **Check:** `python scripts/dev.py gateway identity catalog` → the three-terminal test from 0.8.3 passes.

---

## 0.12 Docs & Architecture Decision Records (ADRs)

🎯 **Goal:** a `README.md` and the first ADRs, so the *why* behind decisions survives your memory.

🧠 **Why ADRs?** Six months from now you will not remember why you didn't use OpenIddict or MediatR. An ADR is a half-page note: *context → decision → consequences*. Employers reading your portfolio repo will read these first — they prove you think, not just type.

`docs/adr/0000-template.md`:

```markdown
# ADR-NNNN: <short title>
Date: YYYY-MM-DD · Status: Proposed | Accepted | Superseded by ADR-XXXX
## Context      (what problem, what constraints)
## Decision     (what we chose — one clear sentence, then details)
## Alternatives (what else we considered and why not)
## Consequences (good and bad; what we must do because of this)
```

**Write these now** (each is just Part A of this guide, in your own words, ~10 lines): ADR-001 .NET 10 · ADR-002 No MediatR/AutoMapper · ADR-003 MassTransit v8 pinned & the Phase 7 licence decision · ADR-004 Hand-built Identity before OpenIddict · ADR-005 Schema-per-service with per-service DB roles · ADR-006 Postgres ILIKE now, Meilisearch in Phase 3 · ADR-007 Object storage choice deferred (MinIO archived) · ADR-008 Explicit checkout saga (process manager).

`README.md` must contain: what LankaMart is (3 lines), architecture diagram (paste the ASCII from your Basic Architecture doc), prerequisites, "quick start" (`cp .env.example .env`, `docker compose up -d`, `python scripts/dev.py`), the port table, and a link to `docs/`.

---

## 0.13 PHASE 0 GATE — definition of done

Do not start Phase 1 until **every** box is ticked:

- [ ] `dotnet --version` is 10.x; Docker, Node, Flutter (`flutter doctor`), Python verified
- [ ] Private GitHub repo with `.gitignore`, `.gitattributes`, protected `main`
- [ ] Folder skeleton exists; solution builds (`dotnet build` → 0 errors)
- [ ] `docker compose up -d` → Postgres, Redis, RabbitMQ **healthy**; schemas `identity…payments` exist; the *permission denied* isolation test passes
- [ ] BuildingBlocks compiled; Money/Phone unit tests pass
- [ ] Gateway forwards `/api/v1/identity/ping` and `/api/v1/catalog/ping`; 404 returns JSON problem details
- [ ] Gateway container builds and runs; you can explain why the first run returned 502
- [ ] CI is green on `main`; you saw it go red once on purpose
- [ ] `python scripts/dev.py` starts everything
- [ ] README + ADR-001…008 committed
- [ ] `git tag phase-0-complete && git push --tags`

**Self-quiz (answer out loud before continuing):**
1. Why does each service get its own DB login even though they share one Postgres?
2. What is the difference between `/health/live` and `/health/ready`?
3. Why is `localhost` different inside a container?
4. Why is money stored as `long` cents?
5. Why does `AddPlatformAuth` set a *fallback policy* that requires authentication?

---

## 0.14 Phase 0 troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `docker compose up` says port in use | Local Postgres/Redis already running | Stop it or change the host port (left side of `5432:5432`) and your connection strings |
| Postgres init script "didn't run" | Data volume already existed | `docker compose down -v` then `up -d` (deletes data) |
| `permission denied for schema` when **your service** starts | You're using the wrong DB login for that service | Connection string username must be `<service>_svc` (Appendix B) |
| `NU1202` / package incompatible | Wrong version line | Re-add the package with `--version "10.*"` (or `"8.*"` for MassTransit) |
| Gateway 502 | Target service down / wrong port | Start the service; check `Clusters` addresses |
| `dotnet run` ignores `--urls` | Launch profile overrides | Add `--no-launch-profile` |
| Windows: scripts fail with `\r` errors | CRLF line endings in `.sh` | `.gitattributes` fixes new clones; run `git add --renormalize .` |
| Docker on Windows is very slow | Repo on a Windows drive mounted into WSL | Keep the repo inside the WSL filesystem (`~/lankamart`) if you use WSL |

---

# PART C — PHASE 1: THE CORE BUYING LOOP (Weeks 3–10)

> **Roadmap definition (architecture doc §9):** *IAM · User · Catalog · Inventory · Order (cart → checkout saga → state machine → tracking) · Payment (mock gateway first) · React storefront · single Postgres (schema-per-service). **Gate: you can register, buy with mock payment, stock decrements, order tracks. Demo-able.***

---

## 1.1 Goal, scope, and what "done" means

🎯 **The one sentence:** *A person can open the website, register and verify their phone and email, search for a product a seller listed, put it in the cart, check out with a fake card, see the stock go down, and watch the order status update.*

**The two stories you are implementing** (from System Scenarios Threads A & parts of §2–3):

* **Seller story:** register (email + phone OTP) → dashboard in "view-only" mode → submit KYC → **Seller Manager approves** → create product with variants → set stock.
* **Customer story:** browse/search → add to cart (as a guest, too) → register/login → checkout with idempotency → pay with the fake gateway (`4111…` succeeds, `4000 0000 0000 0002` declines, amount ending `.99` goes *pending* for 30 s) → order `PAID` → track.

### Scope table

| ✅ IN Phase 1 | ⏭️ OUT (and when it arrives) |
|---|---|
| Gateway (YARP), Identity, Users, Catalog, Inventory, Orders, Payments (mock gateway) | Notification service (Phase 2 — OTPs are *logged* for now) |
| Roles: guest, customer, seller, seller_manager, super_admin (dev accounts) | Rider, customer_manager, audit_manager flows (Phase 4–6; roles are already reserved in the enum) |
| Email + phone OTP verification (codes shown in logs/dev endpoint) | Real SMS/email providers (Phase 2) |
| Seller KYC **form data** + manager approval state machine | KYC **document uploads** & Media service (Phase 3) |
| Products, variants, categories, images **as URLs**, search by `ILIKE` | Meilisearch, SEO, moderation queue, reviews, 3-language content (Phase 3) |
| Redis stock counters + Lua reservations + TTL sweeper | Reconciler worker (Phase 2), outbox (Phase 2) |
| Cart (guest + user), checkout, order state machine, tracking timeline | Real logistics: pickup/delivery/COD/POD (Phase 4) — a **dev simulator** advances statuses |
| Fake payment gateway + webhook + double-entry ledger (charge only) | PayHere, escrow release, commission, payouts, refunds (Phases 2/4/5) |
| React storefront, thin Flutter slice, Python related-products service | Seller portal UI, admin panels, full Flutter apps (Phases 3–7) |
| Unit + integration tests, CI, Docker images | Kubernetes, GitOps, load tests (Phase 2) |

### Phase 1 definition of done (summary — the full checklist is §1.20)
Everything in the scope table's left column works end-to-end **on a fresh machine** using `docker compose up -d` + `python scripts/dev.py` + `python scripts/seed_demo.py`, CI is green, and the failure paths (decline, timeout, double-click, duplicate webhook, out-of-stock) behave correctly.

---

## 1.2 Concepts you must understand first

Read this section **before** writing code. Each concept is used repeatedly.

### 1.2.1 REST in five minutes
A REST API exposes **resources** (nouns, plural) and uses HTTP **verbs** for actions.

| Verb | Meaning | Safe? | Idempotent?* | Example |
|---|---|---|---|---|
| `GET` | read | yes | yes | `GET /api/v1/products?query=shoe` |
| `POST` | create / run an action | no | **no** | `POST /api/v1/orders` |
| `PUT` | replace | no | yes | `PUT /api/v1/customers/me` |
| `PATCH` | partial update | no | usually | `PATCH /api/v1/cart/items/{id}` |
| `DELETE` | remove | no | yes | `DELETE /api/v1/cart/items/{id}` |

\*Idempotent = doing it twice has the same effect as once. `POST` isn't — that's why checkout needs an **Idempotency-Key** (§1.2.7).

**Status codes you will use:** `200 OK` · `201 Created` · `202 Accepted` (started, finishing later) · `204 No Content` · `400` malformed · `401` not authenticated · `403` authenticated but forbidden · `404` not found (we also use it to **hide** other people's resources) · `409` conflict (duplicate/state) · `422` business rule violated · `429` too many requests · `500` our bug · `503` dependency down.

### 1.2.2 Authentication vs authorization; JWT
* **Authentication (AuthN)** = *who are you?* (login → token).
* **Authorization (AuthZ)** = *what may you do?* (roles + rules).
* A **JWT** is a signed JSON blob: `header.payload.signature`. The payload holds **claims** (`sub` = user id, `role`, `exp` = expiry). Any service with the **public key** can verify the signature *without calling Identity* — that's why microservices use them. **Signed ≠ encrypted**: anyone can *read* the payload (paste one into jwt.io), so never put secrets in it.
* **Access token** = short-lived (15 min) JWT sent on every request. **Refresh token** = long-lived (7 days) opaque random string used only to get a new access token. Short access tokens limit damage if stolen; refresh tokens can be **revoked** server-side.
* **Refresh rotation with reuse detection:** every refresh returns a *new* refresh token and invalidates the old. If an *old* one is presented again, either the client is buggy or it was **stolen** → we revoke the whole token family and force re-login.

### 1.2.3 RBAC vs resource policies, and IDOR
* **RBAC (role-based):** "sellers may call `POST /seller/products`". Coarse. Checked at the gateway and again in the service.
* **Resource policy:** "…but only on **their own** products". Fine-grained. Checked in the service using data from the database.
* **IDOR** (Insecure Direct Object Reference / BOLA): `GET /orders/123` returning someone else's order because you checked *"is logged in"* but not *"does it belong to me"*. It is the #1 marketplace vulnerability (architecture doc §6.3). **Rule: every query for user-owned data filters by the caller's id from the token — never by an id the client sends about themselves.** For other people's resources return **404**, not 403, so attackers can't tell what exists.

### 1.2.4 Entities, DTOs, and mass assignment
* **Entity** = a class mapped to a database table. **DTO** (data transfer object) = a class for what goes over the wire. **Never accept an entity directly from the client**: if `User` has `IsAdmin`, an attacker sends `{"isAdmin": true}` and you saved it (*mass assignment*, OWASP API3). Always bind to a DTO that lists exactly the allowed fields.

### 1.2.5 EF Core & migrations
**EF Core** maps C# classes ↔ tables. **Code-first migrations:** you change the C# model, run `dotnet ef migrations add <Name>`, and EF generates a C# file with `Up()`/`Down()` that alters the database; at startup (dev) we apply them. Migrations are **committed to Git** — they are the history of your schema.

### 1.2.6 Events, at-least-once delivery, idempotent consumers
* A **command** says "do this" (`ReserveStock`) — one receiver, can fail. An **event** says "this happened" (`OrderPaid`) — zero-to-many listeners, the publisher doesn't care who.
* **Synchronous (REST)** for *queries* and for the one step where the caller needs the answer right now (reserve stock). **Asynchronous (events)** for *side effects* (notify, record analytics, decrement stock).
* RabbitMQ guarantees **at-least-once** delivery: after a crash the *same* message may arrive twice. Therefore every consumer must be **idempotent**: processing twice = processing once. Techniques you will use: *upsert*, *"if already done, return"*, *state guards* (`UPDATE … WHERE status='PENDING'`), *unique constraints*.
* Messages that keep failing go to an **error queue** (dead-letter) so one poison message doesn't block everything; you inspect and replay them.

### 1.2.7 Idempotency keys
Mobile networks drop connections. The customer taps *Pay*, the request reaches the server, the response is lost, the app retries → **two orders**. Fix: the client generates a UUID per *checkout attempt* and sends `Idempotency-Key: <uuid>`. The server remembers `(user, key) → result`; the retry returns the same order instead of creating another.

### 1.2.8 Saga & compensation
A checkout touches three services (Orders, Inventory, Payments) and **there is no distributed transaction** (Principle #4). Instead we run steps in order and, if a later step fails, run **compensating actions** that undo earlier ones: payment fails → release the reserved stock → cancel the order. The order row stores the saga's state, so a crash mid-checkout can resume (a sweeper cleans up stragglers).

### 1.2.9 Eventual consistency & the dual-write problem
After payment, *Payments* says "completed", *Orders* updates a moment later, *Inventory* a moment after that. For seconds, services disagree. That's fine as long as the system **converges** and the critical bits (money, stock) are protected.
The dangerous spot is the **dual write**: "save to my database **and** publish an event". If the process dies between the two, they diverge forever. In Phase 1 we publish directly and *accept* this risk (with TTL sweepers as safety nets). **Phase 2 adds the Transactional Outbox** (architecture doc §5.2) which removes it. Understand the problem now; the fix will make sense later.

---

## 1.3 Phase 1 architecture

```
 React (5173)         Flutter app            Python recommender (5110)
      │ (Vite proxy)        │                        ▲
      └───────────┬─────────┘                        │ REST (public catalog API only)
                  ▼                                  │
        ┌─────────────────────────────┐              │
        │ GATEWAY (YARP) :5100        │──────────────┘
        │ JWT check • rate limit •    │
        │ CORS • logout blacklist     │      /api/v1/internal/**  →  NOT ROUTED (404)
        └──┬───────┬───────┬───────┬──┘
           │       │       │       │      (REST, public paths)
 ┌─────────▼┐ ┌────▼────┐ ┌▼──────┐ ┌────▼────┐ ┌───────────┐ ┌───────────┐
 │ IDENTITY │ │  USERS  │ │CATALOG│ │ORDERS   │ │ INVENTORY │ │ PAYMENTS  │
 │  :5101   │ │  :5102  │ │ :5103 │ │ :5105   │ │  :5104    │ │  :5106    │
 └────┬─────┘ └────┬────┘ └───┬───┘ └──┬──────┘ └─────┬─────┘ └─────┬─────┘
      │ schema      │ schema  │ schema │ schema       │ schema+Redis │ schema
      └─────────────┴─────────┴───┬────┴──────────────┴──────────────┘
                                  │      ONE Postgres, six schemas, six logins
        ┌─────────────────────────┴──────────────────────────────┐
        │ RabbitMQ (events)     Redis (stock, carts, OTP, idem)   │
        └─────────────────────────────────────────────────────────┘
 Orders ──internal REST──► Catalog (price lookup)  ·  Inventory (reserve)  ·  Payments (intent)
```

### 1.3.1 The three flows you are building

**Flow 1 — Registration & verification**
```
Client → Gateway → Identity: POST /auth/register
  Identity: create user (password hashed), assign role, store OTPs in Redis (email + phone)
  Identity: LOG the OTP codes (dev)  ·  publish UserRegistered
  Users (consumer): create CustomerProfile / SellerProfile(Registered)         [async]
Client → Identity: POST /auth/verify-otp (email) ; POST /auth/verify-otp (phone)
Client → Identity: POST /auth/login → { accessToken, refreshToken }  (claims: email_verified, phone_verified)
```

**Flow 2 — Seller onboarding**
```
Seller (verified) → Users: POST /sellers/kyc → status KycSubmitted
Seller Manager → Users: POST /admin/sellers/{id}/approve → status Active → publish SellerApproved
Catalog (consumer): KnownSellers[seller] = Active    ← now POST /seller/products is allowed
Seller → Catalog: create product + variants → publish VariantCreated
Inventory (consumer): create InventoryItem(variant, qty 0)    ← seller sets stock via /seller/inventory/adjust
```

**Flow 3 — Checkout (the heart of the system)**
```
Client → Orders: POST /orders  (Idempotency-Key)
 1. Idempotency check (Redis)        2. Read cart (Redis)
 3. Catalog.lookup(variants) → authoritative price/name/seller (REST, internal)
 4. Create Order PENDING_PAYMENT + items + shipments (address snapshotted)  ← saga state persisted
 5. Inventory.reserve(orderId, lines, ttl)   [Lua, atomic]
      fail → order CANCELLED (OUT_OF_STOCK)  → 409 to client
 6. Payments.createIntent(orderId, total)
      fail → COMPENSATE: order CANCELLED + publish OrderCancelled (Inventory releases)
 7. 201 {order, paymentId}; publish OrderCreated
Client → Payments: POST /payments/{id}/pay (card)   → 202 "processing"
Mock gateway → (after 2 s, or 30 s if amount ends .99) → Payments webhook (HMAC-signed)
Payments: record ledger, publish PaymentCompleted | PaymentFailed
Orders (consumer):  Completed → order PAID → publish OrderPaid → Inventory confirms reservation
                    Failed    → order PAYMENT_FAILED → publish OrderCancelled → Inventory releases, cart restored
Sweeper: PENDING_PAYMENT older than 15 min → EXPIRED → OrderCancelled (release)
```

### 1.3.2 Conventions every service follows
* **URLs:** `/api/v1/<plural-noun>`; admin under `/api/v1/admin/...`; seller-scoped under `/api/v1/seller/...`; service-to-service under `/api/v1/internal/...` (never routed by the gateway).
* **Errors:** RFC 7807 Problem Details JSON with `code` and `traceId` (BuildingBlocks does this).
* **JSON:** `camelCase` property names (ASP.NET default); enums as strings; money as integer `…Cents` fields; timestamps ISO-8601 UTC.
* **Pagination:** `?page=1&page_size=20` (cap 50) → `{ items, page, pageSize, total }`. *(We use JSON property names `page`, `pageSize`, `total`; the query parameter is `page_size` — see the `[FromQuery(Name=…)]` in §1.9.)*
* **IDs:** `Guid.CreateVersion7()` (time-ordered GUIDs → friendlier to database indexes than random v4).
* **Config:** appsettings + environment variables; **secrets never in Git** (dev passwords are throwaway and local-only).
* **Every state change that others care about emits an event** (Principle #7).
* **Inside each service** (single project, folders — not four projects, to keep a beginner's mental load low):

```
LankaMart.<Svc>.Api/
├── Domain/            entities + business rules (no EF, no HTTP in here if you can help it)
├── Data/              DbContext, EF configuration, Migrations/
├── Features/          one file per use-case: endpoints + handlers + DTOs + validators
├── Consumers/         MassTransit event consumers
├── Clients/           typed HTTP clients to other services (Orders only)
├── Workers/           BackgroundService sweepers
├── GlobalUsings.cs    global using LankaMart.BuildingBlocks; global using LankaMart.Contracts.V1;
├── appsettings.json   non-secret defaults        appsettings.Development.json   local dev values
└── Program.cs         wiring only
```

---

## 1.4 Data ownership & database design

**One Postgres database `lankamart`, six schemas, six logins.** A service may touch **only its own schema**. Everything it needs from others arrives via API calls or by **keeping a small local copy built from events** (e.g., Catalog's `known_sellers`).

| Service | Schema / login | Tables (Phase 1) | Source of truth for | Redis keys |
|---|---|---|---|---|
| Identity | `identity` / `identity_svc` | `users`, `roles`, `user_roles`, `refresh_tokens` (+ Identity's own tables) | credentials, roles, verification flags | `otp:*`, `bl:{jti}` (blacklist) |
| Users | `users` / `users_svc` | `customer_profiles`, `addresses`, `seller_profiles` | profile data, seller status & KYC | — |
| Catalog | `catalog` / `catalog_svc` | `categories`, `category_translations`, `products`, `product_translations`, `product_variants`, `product_images`, `known_sellers` | what is for sale, prices | — |
| Inventory | `inventory` / `inventory_svc` | `inventory_items`, `reservations`, `reservation_lines`, `stock_movements` | stock quantities | `stock:{variantId}` counters |
| Orders | `orders` / `orders_svc` | `orders`, `order_items`, `shipments`, `order_events` | order lifecycle | `cart:*`, `idem:orders:*` |
| Payments | `payments` / `payments_svc` | `payments`, `ledger_transactions`, `ledger_entries`, `webhook_events` | money movement | — |

**Cross-service references are plain IDs without foreign keys** — e.g., `orders.order_items.variant_id` is just a GUID; there is no FK to `catalog.product_variants` (different schema, different owner). Referential integrity across services is *a business-level concern*, handled by events and validation, not by the database.

**Snapshots (architecture doc §4.5):** an order stores the product name, unit price, and delivery address **as they were at purchase**. Never reference mutable data from an immutable record — otherwise changing a price tomorrow rewrites last month's invoices.

**Money columns:** `bigint` named `*_cents`. **Enums:** stored as readable strings. **Timestamps:** `timestamptz`, always UTC.

---

## 1.5 Shared contracts (events + internal DTOs)

🎯 **Goal:** one project that defines every message shape used between services.

🧠 **Why shared?** MassTransit routes by the *type name* (namespace + class). Publisher and consumer must reference the *same* type. This is the "versioned contracts" rule (Principle #5): the namespace carries the version — `V1`. **Rules for evolving them:** you may *add* optional fields; you must **never rename, remove, or change the meaning** of a field. A breaking change = a new type in a `V2` namespace, published alongside V1 until all consumers move.

`src/shared/LankaMart.Contracts/Events.cs`:

```csharp
namespace LankaMart.Contracts.V1;

// ───────────── Identity ─────────────
public sealed record UserRegistered(Guid UserId, string Email, string Phone, string FullName, string Role, string Language, DateTimeOffset OccurredAt);
public sealed record UserContactVerified(Guid UserId, string Channel /* "email" | "phone" */, DateTimeOffset OccurredAt);
public sealed record UserLoginFailed(Guid? UserId, string Email, string? Ip, DateTimeOffset OccurredAt);   // Security service consumes this in Phase 6

// ───────────── Users ─────────────
public sealed record SellerKycSubmitted(Guid SellerId, string BusinessName, DateTimeOffset OccurredAt);
public sealed record SellerApproved(Guid SellerId, string BusinessName, DateTimeOffset OccurredAt);
public sealed record SellerRejected(Guid SellerId, string ReasonCode, DateTimeOffset OccurredAt);
public sealed record SellerSuspended(Guid SellerId, string Reason, DateTimeOffset OccurredAt);
public sealed record SellerReinstated(Guid SellerId, DateTimeOffset OccurredAt);

// ───────────── Catalog ─────────────
public sealed record VariantCreated(Guid VariantId, Guid ProductId, Guid SellerId, string Sku, DateTimeOffset OccurredAt);
public sealed record ProductStatusChanged(Guid ProductId, Guid SellerId, string OldStatus, string NewStatus, DateTimeOffset OccurredAt);

// ───────────── Inventory ─────────────
public sealed record InventoryLowStock(Guid VariantId, Guid SellerId, int Available, int ReorderPoint, DateTimeOffset OccurredAt);
public sealed record ReservationConfirmFailed(Guid OrderId, string Reason, DateTimeOffset OccurredAt);

// ───────────── Orders ─────────────
public sealed record OrderLine(Guid VariantId, Guid SellerId, int Quantity, long UnitPriceCents);
public sealed record OrderCreated(Guid OrderId, string OrderNumber, Guid CustomerId, long TotalCents, DateTimeOffset OccurredAt);
public sealed record OrderPaid(Guid OrderId, string OrderNumber, Guid CustomerId, long TotalCents, IReadOnlyList<OrderLine> Lines, DateTimeOffset OccurredAt);
public sealed record OrderCancelled(Guid OrderId, Guid CustomerId, string Reason, IReadOnlyList<OrderLine> Lines, DateTimeOffset OccurredAt);
public sealed record OrderStatusChanged(Guid OrderId, Guid CustomerId, string From, string To, DateTimeOffset OccurredAt);
public sealed record OrderPaymentOrphaned(Guid OrderId, Guid PaymentId, DateTimeOffset OccurredAt);   // paid after the order expired/cancelled → needs refund (Phase 5)

// ───────────── Payments ─────────────
public sealed record PaymentCompleted(Guid PaymentId, Guid OrderId, Guid CustomerId, long AmountCents, string Currency, string GatewayRef, DateTimeOffset OccurredAt);
public sealed record PaymentFailed(Guid PaymentId, Guid OrderId, Guid CustomerId, string FailureCode, DateTimeOffset OccurredAt);
```

`src/shared/LankaMart.Contracts/InternalApi.cs` — request/response shapes for **service-to-service REST** calls:

```csharp
namespace LankaMart.Contracts.V1.Internal;

// Catalog: POST /api/v1/internal/variants/lookup
public sealed record VariantLookupRequest(IReadOnlyList<Guid> VariantIds);
public sealed record VariantInfo(Guid VariantId, Guid ProductId, Guid SellerId, string Sku, string ProductName,
                                 string? ImageUrl, long PriceCents, bool IsPurchasable);
public sealed record VariantLookupResponse(IReadOnlyList<VariantInfo> Variants);

// Inventory: POST /api/v1/internal/inventory/reserve | confirm | release
public sealed record StockLine(Guid VariantId, int Quantity);
public sealed record ReserveStockRequest(Guid ReservationId, IReadOnlyList<StockLine> Lines, int TtlSeconds);
public sealed record ReserveStockResponse(bool Success, IReadOnlyList<Guid> UnavailableVariantIds);

// Payments: POST /api/v1/internal/payments/intent
public sealed record CreatePaymentIntentRequest(Guid OrderId, Guid CustomerId, long AmountCents, string Currency);
public sealed record CreatePaymentIntentResponse(Guid PaymentId, string Gateway);
```

In **each service** create `GlobalUsings.cs`:
```csharp
global using LankaMart.BuildingBlocks;
global using LankaMart.Contracts.V1;
global using LankaMart.Contracts.V1.Internal;
global using MassTransit;
```

✅ **Check:** `dotnet build` passes. Commit: `feat(contracts): v1 events and internal DTOs`.

---

## 1.6 Identity service

**Port 5101 · schema `identity` · login `identity_svc`.**
**Mission:** register users, verify email/phone, authenticate, issue/rotate/revoke tokens, publish the public key. It is the **trust root** — if it's compromised, everything is. Keep it small.

**Endpoints**

| Method & path | Auth | Purpose |
|---|---|---|
| `POST /api/v1/auth/register` | anonymous | Create account (role `customer` or `seller` only) → sends OTPs |
| `POST /api/v1/auth/verify-otp` | anonymous | Verify email or phone code |
| `POST /api/v1/auth/resend-otp` | anonymous | Re-send a code (60 s cooldown) |
| `POST /api/v1/auth/login` | anonymous | → `{accessToken, refreshToken, expiresIn}` |
| `POST /api/v1/auth/refresh` | anonymous | Rotate refresh token |
| `POST /api/v1/auth/logout` | **token** | Revoke refresh family + blacklist the access token's `jti` |
| `GET  /api/v1/auth/me` | **token** | What this token can do (roles, verification flags) — drives the UI |
| `GET  /.well-known/openid-configuration` and `/.well-known/jwks.json` | anonymous | Public key discovery for other services |
| `GET  /api/v1/dev/otp/{destination}` | dev only | Read the latest OTP (so you can test without SMS) |

### 1.6.1 Packages & config

```bash
cd src/services/identity/LankaMart.Identity.Api
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version "10.*"
dotnet add package Microsoft.EntityFrameworkCore.Design --version "10.*"
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version "10.*"
dotnet add package EFCore.NamingConventions --version "10.*"      # snake_case tables/columns (nicer in Postgres). If it won't resolve for your EF version, remove the .UseSnakeCaseNamingConvention() calls — everything still works.
cd ../../../..
```

`appsettings.json`:
```json
{
  "AllowedHosts": "*",
  "Auth": { "Issuer": "lankamart-identity", "Audience": "lankamart-api" },
  "Jwt":  { "AccessTokenMinutes": 15, "RefreshTokenDays": 7, "KeyPath": "keys/jwt-private.pem" },
  "Otel": { "Endpoint": "" }
}
```
`appsettings.Development.json`:
```json
{
  "ConnectionStrings": { "Default": "Host=localhost;Port=5432;Database=lankamart;Username=identity_svc;Password=identity_dev_pw" },
  "Redis": { "ConnectionString": "localhost:6379" },
  "RabbitMq": { "Host": "localhost", "Port": 5672, "Username": "lankamart", "Password": "lankamart_dev_pw" },
  "Auth": { "Authority": "http://localhost:5101", "RequireHttps": false },
  "Otel": { "Endpoint": "http://localhost:4317" },
  "Seed": {
    "SuperAdminEmail": "admin@lankamart.local",       "SuperAdminPassword": "Admin#12345678",
    "SellerManagerEmail": "sellermgr@lankamart.local", "SellerManagerPassword": "Manager#12345678"
  }
}
```
*(Set `"Otel": {"Endpoint": ""}` if you didn't start Jaeger.)*

### 1.6.2 Domain & database

`Domain/Entities.cs`
```csharp
using Microsoft.AspNetCore.Identity;
namespace LankaMart.Identity.Api.Domain;

public class AppUser : IdentityUser<Guid>      // gives us: Email, PhoneNumber, EmailConfirmed, PhoneNumberConfirmed, lockout fields, password hash
{
    public string FullName { get; set; } = "";
    public string Language { get; set; } = "en";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }            // every token descended from one login shares a family
    public string TokenHash { get; set; } = "";   // SHA-256 of the token — the raw token is NEVER stored (DB leak ≠ session theft)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
}
```

`Data/IdentityDb.cs`
```csharp
using LankaMart.Identity.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Identity.Api.Data;

public class IdentityDb(DbContextOptions<IdentityDb> options) : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("identity");
        b.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.HasIndex(x => x.UserId);
        });
        b.Entity<AppUser>().HasIndex(u => u.PhoneNumber).IsUnique().HasFilter("phone_number IS NOT NULL");   // one account per phone
    }
}
```
*(If you removed the snake-case convention, change the filter to `"\"PhoneNumber\" IS NOT NULL"`.)*

### 1.6.3 Signing keys and tokens

`Security/RsaKeyProvider.cs`
```csharp
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LankaMart.Identity.Api.Security;

public sealed class JwtIssuingOptions
{
    public string KeyPath { get; set; } = "keys/jwt-private.pem";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

/// <summary>Loads the RSA private key from a PEM file, creating it on first run (DEV ONLY — production injects it from Vault / a K8s secret).
/// Tokens are signed with the PRIVATE key (only Identity has it) and verified with the PUBLIC key (everyone can fetch it).</summary>
public sealed class RsaKeyProvider
{
    public RsaSecurityKey SigningKey { get; }
    public RSAParameters PublicParameters { get; }
    public string KeyId => SigningKey.KeyId!;

    public RsaKeyProvider(IOptions<JwtIssuingOptions> opt, ILogger<RsaKeyProvider> log)
    {
        var path = opt.Value.KeyPath;
        var rsa = RSA.Create(2048);
        if (File.Exists(path)) rsa.ImportFromPem(File.ReadAllText(path));
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem());
            log.LogWarning("Generated a NEW JWT signing key at {Path}. Never commit it (it's in .gitignore).", path);
        }
        PublicParameters = rsa.ExportParameters(false);
        var kid = Base64UrlEncoder.Encode(SHA256.HashData(PublicParameters.Modulus!))[..16];   // key id lets you rotate keys later
        SigningKey = new RsaSecurityKey(rsa) { KeyId = kid };
    }
}
```

`Security/TokenService.cs`
```csharp
using System.Security.Cryptography;
using System.Text;
using LankaMart.Identity.Api.Data;
using LankaMart.Identity.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LankaMart.Identity.Api.Security;

public sealed class TokenService(RsaKeyProvider keys, IOptions<JwtIssuingOptions> jwt, IOptions<AuthOptions> auth, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();
    public int AccessTokenSeconds => jwt.Value.AccessTokenMinutes * 60;

    public string CreateAccessToken(AppUser user, IList<string> roles)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(),
            ["email"] = user.Email!,
            ["email_verified"] = user.EmailConfirmed,
            ["phone_verified"] = user.PhoneNumberConfirmed,
            ["role"] = roles.ToArray(),                          // array claim → several "role" claims on the other side
            ["jti"] = Guid.NewGuid().ToString("N"),              // unique id → lets us blacklist this exact token at logout
            ["amr"] = new[] { "pwd" }                            // how the user authenticated (MFA adds "otp" in Phase 3/6)
        };
        if (roles.Contains(Roles.Seller)) claims["seller_id"] = user.Id.ToString();   // seller id == user id, by design (the "tenant" claim)

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = auth.Value.Issuer,
            Audience = auth.Value.Audience,
            Claims = claims,
            IssuedAt = now, NotBefore = now,
            Expires = now.AddMinutes(jwt.Value.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(keys.SigningKey, SecurityAlgorithms.RsaSha256)
        };
        return _handler.CreateToken(descriptor);
    }

    /// <summary>Opaque random refresh token. Returns the RAW value (sent to the client once) and the entity holding only its hash.</summary>
    public (string Raw, RefreshToken Entity) NewRefreshToken(Guid userId, Guid familyId, string? ip, string? userAgent)
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));    // 256 bits of randomness
        var now = clock.GetUtcNow();
        return (raw, new RefreshToken
        {
            Id = Guid.CreateVersion7(), UserId = userId, FamilyId = familyId, TokenHash = Hash(raw),
            CreatedAt = now, ExpiresAt = now.AddDays(jwt.Value.RefreshTokenDays),
            CreatedByIp = ip, UserAgent = userAgent?.Length > 300 ? userAgent[..300] : userAgent
        });
    }

    public static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
```

### 1.6.4 One-time passwords (OTP)

`Security/OtpService.cs`
```csharp
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace LankaMart.Identity.Api.Security;

public interface IOtpSender { Task SendAsync(string channel, string destination, string code); }

/// <summary>DEV sender: writes the code to the log. Phase 2 replaces this with a Notification-service call (SMS/email).
/// ⚠ Never log OTPs in production — anyone with log access could take over accounts.</summary>
public sealed class LogOtpSender(ILogger<LogOtpSender> log) : IOtpSender
{
    public Task SendAsync(string channel, string destination, string code)
    {
        log.LogWarning("[DEV OTP] {Channel} → {Destination}: {Code}", channel, destination, code);
        return Task.CompletedTask;
    }
}

public sealed class OtpService(IConnectionMultiplexer redis, IOtpSender sender, IHostEnvironment env)
{
    private IDatabase Db => redis.GetDatabase();
    private const int MaxAttempts = 5;

    public async Task SendAsync(Guid userId, string channel, string destination)
    {
        // 60-second cooldown: stops someone using your endpoint to spam a victim's phone (SMS costs money, too)
        if (!await Db.StringSetAsync($"otp:cd:{userId}:{channel}", "1", TimeSpan.FromSeconds(60), When.NotExists)) return;

        var code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();   // cryptographic RNG, never `new Random()`
        var key = $"otp:{userId}:{channel}";
        await Db.HashSetAsync(key, [new HashEntry("hash", Hash(code)), new HashEntry("attempts", 0)]);
        await Db.KeyExpireAsync(key, TimeSpan.FromMinutes(5));
        await sender.SendAsync(channel, destination, code);
        if (env.IsDevelopment()) await Db.StringSetAsync($"dev:otp:{destination}", code, TimeSpan.FromMinutes(5));
    }

    public async Task<bool> VerifyAsync(Guid userId, string channel, string code)
    {
        var key = $"otp:{userId}:{channel}";
        if (!await Db.KeyExistsAsync(key)) return false;
        if (await Db.HashIncrementAsync(key, "attempts") > MaxAttempts) { await Db.KeyDeleteAsync(key); return false; }   // brute-force limit: 1,000,000 codes / 5 tries
        string? stored = await Db.HashGetAsync(key, "hash");
        if (stored is null) return false;
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(code)), Encoding.UTF8.GetBytes(stored));   // constant-time compare
        if (ok) await Db.KeyDeleteAsync(key);
        return ok;
    }

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
```

### 1.6.5 The auth logic

`Features/AuthService.cs`
```csharp
using FluentValidation;
using LankaMart.Identity.Api.Data;
using LankaMart.Identity.Api.Domain;
using LankaMart.Identity.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace LankaMart.Identity.Api.Features;

// ── DTOs: exactly the fields a client may send (mass-assignment defence) ──
public sealed record RegisterRequest(string Email, string Phone, string Password, string FullName, string Role = "customer", string Language = "en");
public sealed record LoginRequest(string Email, string Password);
public sealed record VerifyOtpRequest(string Email, string Channel, string Code);
public sealed record ResendOtpRequest(string Email, string Channel);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType = "Bearer");

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Phone).Must(p => PhoneNumber.TryNormalizeLk(p, out _)).WithMessage("Enter a valid Sri Lankan mobile number.");
        RuleFor(x => x.Password).MinimumLength(10).MaximumLength(100);
        RuleFor(x => x.FullName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Role).Must(r => r is Roles.Customer or Roles.Seller).WithMessage("Role must be 'customer' or 'seller'.");   // ← NEVER allow admin roles here
        RuleFor(x => x.Language).Must(l => l is "en" or "si" or "ta");
    }
}
public sealed class LoginValidator : AbstractValidator<LoginRequest> { public LoginValidator() { RuleFor(x => x.Email).NotEmpty().MaximumLength(200); RuleFor(x => x.Password).NotEmpty().MaximumLength(100); } }
public sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpValidator() { RuleFor(x => x.Email).NotEmpty().EmailAddress(); RuleFor(x => x.Channel).Must(c => c is "email" or "phone"); RuleFor(x => x.Code).Matches(@"^\d{6}$"); }
}
public sealed class ResendOtpValidator : AbstractValidator<ResendOtpRequest>
{
    public ResendOtpValidator() { RuleFor(x => x.Email).NotEmpty().EmailAddress(); RuleFor(x => x.Channel).Must(c => c is "email" or "phone"); }
}

public sealed class AuthService(UserManager<AppUser> users, IdentityDb db, TokenService tokens, OtpService otp,
    IPublishEndpoint bus, IConnectionMultiplexer redis, TimeProvider clock, ILogger<AuthService> log)
{
    public async Task RegisterAsync(RegisterRequest r, CancellationToken ct)
    {
        PhoneNumber.TryNormalizeLk(r.Phone, out var phone);            // validator already proved it parses
        var email = r.Email.Trim().ToLowerInvariant();

        if (await users.FindByEmailAsync(email) is not null) throw new ConflictException("EMAIL_TAKEN", "An account with this email already exists.");
        if (await db.Users.AnyAsync(u => u.PhoneNumber == phone, ct)) throw new ConflictException("PHONE_TAKEN", "An account with this phone number already exists.");

        var user = new AppUser { Id = Guid.CreateVersion7(), UserName = email, Email = email, PhoneNumber = phone, FullName = r.FullName.Trim(), Language = r.Language };
        var created = await users.CreateAsync(user, r.Password);       // hashes the password (PBKDF2) — we never see/store it in clear
        if (!created.Succeeded) throw new BusinessRuleException("WEAK_PASSWORD", string.Join(" ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, r.Role);

        await otp.SendAsync(user.Id, "email", email);
        await otp.SendAsync(user.Id, "phone", phone);

        // ⚠ DUAL WRITE: user is saved, event is published separately. If we crash between, the Users service never creates the profile.
        //   Phase 2's Transactional Outbox removes this risk. For now a crash here is rare and visible in logs.
        await bus.Publish(new UserRegistered(user.Id, email, phone, user.FullName, r.Role, r.Language, clock.GetUtcNow()), ct);
    }

    public async Task VerifyContactAsync(VerifyOtpRequest r, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(r.Email.Trim().ToLowerInvariant());
        // same error for "no such user" and "wrong code" → attackers can't use this endpoint to discover which emails are registered
        if (user is null || !await otp.VerifyAsync(user.Id, r.Channel, r.Code)) throw new BusinessRuleException("INVALID_OTP", "Invalid or expired code.");

        if (r.Channel == "email") user.EmailConfirmed = true; else user.PhoneNumberConfirmed = true;
        await users.UpdateAsync(user);
        await bus.Publish(new UserContactVerified(user.Id, r.Channel, clock.GetUtcNow()), ct);
    }

    public async Task ResendOtpAsync(ResendOtpRequest r)
    {
        var user = await users.FindByEmailAsync(r.Email.Trim().ToLowerInvariant());
        if (user is null) return;                                       // always answer 202 → no account enumeration
        await otp.SendAsync(user.Id, r.Channel, r.Channel == "email" ? user.Email! : user.PhoneNumber!);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest r, string? ip, string? ua, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(r.Email.Trim().ToLowerInvariant())
                   ?? throw new UnauthorizedException("INVALID_CREDENTIALS", "Email or password is incorrect.");

        if (await users.IsLockedOutAsync(user)) throw new TooManyRequestsException("ACCOUNT_LOCKED", "Too many failed attempts. Try again in a few minutes.");

        if (!await users.CheckPasswordAsync(user, r.Password))
        {
            await users.AccessFailedAsync(user);                        // 5 failures → locked 10 minutes (configured in Program.cs)
            await bus.Publish(new UserLoginFailed(user.Id, user.Email!, ip, clock.GetUtcNow()), ct);
            throw new UnauthorizedException("INVALID_CREDENTIALS", "Email or password is incorrect.");
        }
        await users.ResetAccessFailedCountAsync(user);
        return await IssueAsync(user, Guid.CreateVersion7(), ip, ua, ct);   // new login = new token family
    }

    public async Task<TokenResponse> RefreshAsync(string rawToken, string? ip, string? ua, CancellationToken ct)
    {
        var hash = TokenService.Hash(rawToken);
        var t = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct)
                ?? throw new UnauthorizedException("INVALID_REFRESH_TOKEN", "Please sign in again.");
        var now = clock.GetUtcNow();

        // ATOMIC CLAIM: only one request can flip used_at from NULL → value. Two simultaneous refreshes can't both win.
        var claimed = await db.RefreshTokens
            .Where(x => x.Id == t.Id && x.UsedAt == null && x.RevokedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now), ct);

        if (claimed == 0)
        {
            if (t.UsedAt != null || t.RevokedAt != null)                // an OLD token came back → assume theft → revoke the whole family
            {
                await db.RefreshTokens.Where(x => x.FamilyId == t.FamilyId && x.RevokedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
                log.LogWarning("Refresh-token REUSE detected: user {UserId}, family {FamilyId} revoked", t.UserId, t.FamilyId);
            }
            throw new UnauthorizedException("INVALID_REFRESH_TOKEN", "Please sign in again.");
        }

        var user = await users.FindByIdAsync(t.UserId.ToString());
        if (user is null || await users.IsLockedOutAsync(user)) throw new UnauthorizedException("INVALID_REFRESH_TOKEN", "Please sign in again.");
        return await IssueAsync(user, t.FamilyId, ip, ua, ct);          // same family, brand-new refresh token
    }

    public async Task LogoutAsync(string rawRefreshToken, string? jti, long? exp, CancellationToken ct)
    {
        var hash = TokenService.Hash(rawRefreshToken);
        var t = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        var now = clock.GetUtcNow();
        if (t is not null)
            await db.RefreshTokens.Where(x => x.FamilyId == t.FamilyId && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);

        // A JWT can't be "un-signed", so we blacklist its jti until it would have expired anyway. The gateway checks this on every request.
        if (jti is not null && exp is not null)
        {
            var ttl = DateTimeOffset.FromUnixTimeSeconds(exp.Value) - now;
            if (ttl > TimeSpan.Zero) await redis.GetDatabase().StringSetAsync($"bl:{jti}", "1", ttl);
        }
    }

    private async Task<TokenResponse> IssueAsync(AppUser user, Guid familyId, string? ip, string? ua, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user);
        var access = tokens.CreateAccessToken(user, roles);
        var (raw, entity) = tokens.NewRefreshToken(user.Id, familyId, ip, ua);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return new TokenResponse(access, raw, tokens.AccessTokenSeconds);
    }
}
```

### 1.6.6 Endpoints

`Features/AuthEndpoints.cs`
```csharp
using LankaMart.Identity.Api.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LankaMart.Identity.Api.Features;

public static class AuthEndpoints
{
    // Behind the gateway the TCP peer is the gateway; YARP puts the real client in X-Forwarded-For.
    // ⚠ Spoofable if clients can reach this service directly → in Phase 6 NetworkPolicy makes the gateway the only caller.
    private static string? ClientIp(HttpContext c) =>
        c.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ?? c.Connection.RemoteIpAddress?.ToString();

    public static void MapAuth(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/auth").WithTags("Auth");

        g.MapPost("/register", async (RegisterRequest r, AuthService s, CancellationToken ct) =>
        {
            await s.RegisterAsync(r, ct);
            return Results.Accepted(value: new { message = "Account created. Enter the codes we sent to your email and phone." });
        }).AllowAnonymous().WithValidation<RegisterRequest>();

        g.MapPost("/verify-otp", async (VerifyOtpRequest r, AuthService s, CancellationToken ct) =>
        { await s.VerifyContactAsync(r, ct); return Results.NoContent(); }).AllowAnonymous().WithValidation<VerifyOtpRequest>();

        g.MapPost("/resend-otp", async (ResendOtpRequest r, AuthService s) =>
        { await s.ResendOtpAsync(r); return Results.Accepted(); }).AllowAnonymous().WithValidation<ResendOtpRequest>();

        g.MapPost("/login", async (LoginRequest r, AuthService s, HttpContext c, CancellationToken ct) =>
            Results.Ok(await s.LoginAsync(r, ClientIp(c), c.Request.Headers.UserAgent, ct))).AllowAnonymous().WithValidation<LoginRequest>();

        g.MapPost("/refresh", async (RefreshRequest r, AuthService s, HttpContext c, CancellationToken ct) =>
            Results.Ok(await s.RefreshAsync(r.RefreshToken, ClientIp(c), c.Request.Headers.UserAgent, ct))).AllowAnonymous();

        g.MapPost("/logout", async (LogoutRequest r, AuthService s, HttpContext c, CancellationToken ct) =>
        {
            long? exp = long.TryParse(c.User.FindFirst("exp")?.Value, out var e) ? e : null;
            await s.LogoutAsync(r.RefreshToken, c.User.FindFirst("jti")?.Value, exp, ct);
            return Results.NoContent();
        });   // requires a valid token (fallback policy)

        g.MapGet("/me", (HttpContext c) => Results.Ok(new
        {
            userId = c.User.UserId(),
            email = c.User.FindFirst("email")?.Value,
            roles = c.User.FindAll("role").Select(x => x.Value),
            emailVerified = c.User.HasFlag("email_verified"),
            phoneVerified = c.User.HasFlag("phone_verified"),
            sellerId = c.User.FindFirst("seller_id")?.Value
        }));
    }

    public static void MapWellKnown(this WebApplication app)
    {
        // Other services (and the gateway) call these to learn how to verify our tokens.
        app.MapGet("/.well-known/openid-configuration", (IOptions<AuthOptions> o) => Results.Ok(new
        {
            issuer = o.Value.Issuer,
            jwks_uri = $"{o.Value.Authority.TrimEnd('/')}/.well-known/jwks.json"
        })).AllowAnonymous();

        app.MapGet("/.well-known/jwks.json", (RsaKeyProvider k) => Results.Ok(new
        {
            keys = new[] { new {
                kty = "RSA", use = "sig", alg = "RS256", kid = k.KeyId,
                n = Base64UrlEncoder.Encode(k.PublicParameters.Modulus), e = Base64UrlEncoder.Encode(k.PublicParameters.Exponent) } }
        })).AllowAnonymous();
    }

    public static void MapDevEndpoints(this WebApplication app)
    {
        // DEV ONLY (called only when IsDevelopment): lets the React app / seed script read OTPs without a real SMS gateway.
        app.MapGet("/api/v1/dev/otp/{destination}", async (string destination, StackExchange.Redis.IConnectionMultiplexer r) =>
        {
            var code = await r.GetDatabase().StringGetAsync($"dev:otp:{destination}");
            return code.HasValue ? Results.Ok(new { destination, code = code.ToString() }) : Results.NotFound();
        }).AllowAnonymous();
    }
}
```

### 1.6.7 Seeding roles and dev admins

`Data/IdentitySeeder.cs`
```csharp
using LankaMart.Identity.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace LankaMart.Identity.Api.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider sp, IHostEnvironment env, IConfiguration cfg)
    {
        using var scope = sp.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        foreach (var r in Roles.All)                                    // all 9 actor roles exist from day one
            if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole<Guid>(r));

        if (!env.IsDevelopment()) return;                               // never auto-create admins outside dev
        await EnsureAsync(users, cfg["Seed:SuperAdminEmail"], cfg["Seed:SuperAdminPassword"], "Super Admin", "+94700000001", Roles.SuperAdmin);
        await EnsureAsync(users, cfg["Seed:SellerManagerEmail"], cfg["Seed:SellerManagerPassword"], "Seller Manager", "+94700000002", Roles.SellerManager);
    }

    private static async Task EnsureAsync(UserManager<AppUser> um, string? email, string? pw, string name, string phone, string role)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pw) || await um.FindByEmailAsync(email) is not null) return;
        var u = new AppUser { Id = Guid.CreateVersion7(), UserName = email, Email = email, EmailConfirmed = true, PhoneNumber = phone, PhoneNumberConfirmed = true, FullName = name };
        if ((await um.CreateAsync(u, pw)).Succeeded) await um.AddToRoleAsync(u, role);
    }
}
```

### 1.6.8 `Program.cs`

```csharp
using FluentValidation;
using LankaMart.Identity.Api.Data;
using LankaMart.Identity.Api.Domain;
using LankaMart.Identity.Api.Features;
using LankaMart.Identity.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("identity");

builder.Services.AddDbContext<IdentityDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "identity"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 10; o.Password.RequireDigit = true; o.Password.RequireLowercase = true;
    o.Password.RequireUppercase = true; o.Password.RequireNonAlphanumeric = false;
    o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
}).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<IdentityDb>();

builder.Services.Configure<JwtIssuingOptions>(cfg.GetSection("Jwt"));
builder.Services.Configure<AuthOptions>(cfg.GetSection("Auth"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RsaKeyProvider>();
builder.Services.AddSingleton<IOtpSender, LogOtpSender>();
builder.Services.AddSingleton<OtpService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformRedis(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "identity");          // Identity only publishes; no consumers

var app = builder.Build();
await app.MigrateDatabaseAsync<IdentityDb>();
await IdentitySeeder.SeedAsync(app.Services, app.Environment, cfg);

app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapWellKnown();
app.MapAuth();
if (app.Environment.IsDevelopment()) app.MapDevEndpoints();
app.Run();

public partial class Program;     // lets integration tests (WebApplicationFactory<Program>) see this class
```

### 1.6.9 Create the migration and run

```bash
dotnet ef migrations add Initial --project src/services/identity/LankaMart.Identity.Api --output-dir Data/Migrations
dotnet run --project src/services/identity/LankaMart.Identity.Api --no-launch-profile   # with ASPNETCORE_ENVIRONMENT=Development and ASPNETCORE_URLS=http://localhost:5101   (or use scripts/dev.py identity)
```

✅ **Check — walk the whole flow with curl (or Bruno)**

```bash
BASE=http://localhost:5101
# 1) register
curl -s -X POST $BASE/api/v1/auth/register -H "Content-Type: application/json" \
  -d '{"email":"nimal@example.com","phone":"0771234567","password":"Sup3rSecret!!","fullName":"Nimal Perera","role":"customer"}'
# 2) read the OTPs from the Identity log (look for [DEV OTP]) or via the dev endpoint
curl -s "$BASE/api/v1/dev/otp/nimal@example.com"
curl -s "$BASE/api/v1/dev/otp/%2B94771234567"        # '+' must be %2B in a URL
# 3) verify both channels
curl -i -X POST $BASE/api/v1/auth/verify-otp -H "Content-Type: application/json" -d '{"email":"nimal@example.com","channel":"email","code":"123456"}'
# 4) login → copy accessToken; paste it into https://jwt.io and READ the claims (sub, role, email_verified…)
curl -s -X POST $BASE/api/v1/auth/login -H "Content-Type: application/json" -d '{"email":"nimal@example.com","password":"Sup3rSecret!!"}'
# 5) call a protected endpoint
curl -i $BASE/api/v1/auth/me                                   # → 401 (no token)
curl -s $BASE/api/v1/auth/me -H "Authorization: Bearer <accessToken>"
# 6) refresh twice with the SAME refresh token → second call must be 401 and revoke the family
# 7) wrong password 5 times → 429 ACCOUNT_LOCKED
curl -s $BASE/.well-known/jwks.json
```
Also look in **pgAdmin / DBeaver** at schema `identity`: you should see `users` (with `password_hash`, **not** the password), `refresh_tokens` (with `token_hash`, **not** the token).

**Mandatory tests for this service (write them in §1.16):** password policy; duplicate email/phone → 409; OTP success, wrong code, 6th attempt locked, expiry; login success/failure/lockout; refresh rotation; **refresh reuse revokes family**; tampered/`alg:none` token rejected; admin role cannot be self-assigned at registration.

⚠️ **Pitfalls**
* **401 on every call in other services later** → their `Auth:Authority` must be the Identity URL and `Auth:Issuer` must equal the token's `iss` (`lankamart-identity`).
* *"The type initializer… RsaKeyProvider"* / permission error → can't write `keys/` folder. Check the working directory and write access.
* Registering twice quickly with the same phone → unique index violation 500? The pre-check handles the normal case; the unique index is the safety net for races (you could catch `DbUpdateException` and map to `PHONE_TAKEN` as an exercise).
* **The refresh-token race:** if a client fires two refresh calls at once with the same token (very common with React strict mode + parallel requests), the second looks like *reuse* and logs the user out. Clients **must serialize refreshes** — the React and Flutter code later implement a "single-flight" refresh. Understand this now; it's the #1 "why do I get randomly logged out?" bug.

Commit: `feat(identity): registration, OTP, JWT, refresh rotation`.

---

## 1.7 Gateway hardening

🎯 **Goal:** replace the Phase 0 demo routes with the real route table and add the gateway's jobs: JWT validation, rate limiting, CORS, logout blacklist, security headers, body-size cap — and prove `/internal/*` is unreachable from outside.

🧠 **Why validate JWTs at the gateway *and* in each service?** *Defence in depth.* The gateway rejects garbage cheaply before it touches a service; each service re-validates because it must never assume "the gateway checked" (a misconfigured route or a compromised neighbour would otherwise bypass everything). **Never trust the network.**

### 1.7.0 The config template every service uses

All services share the same *shape* of `appsettings.Development.json`; only the DB login and a few URLs differ (full table in **Appendix B**). Use this template and change the marked values:

```json
{
  "ConnectionStrings": { "Default": "Host=localhost;Port=5432;Database=lankamart;Username=<svc>_svc;Password=<svc>_dev_pw" },
  "Redis":    { "ConnectionString": "localhost:6379" },
  "RabbitMq": { "Host": "localhost", "Port": 5672, "Username": "lankamart", "Password": "lankamart_dev_pw" },
  "Auth":     { "Authority": "http://localhost:5101", "Issuer": "lankamart-identity", "Audience": "lankamart-api", "RequireHttps": false },
  "Internal": { "ApiKey": "dev-internal-key-change-me" },
  "Services": {
    "Catalog": "http://localhost:5103", "Inventory": "http://localhost:5104",
    "Payments": "http://localhost:5106", "Orders": "http://localhost:5105"
  },
  "Otel": { "Endpoint": "http://localhost:4317" }
}
```
*(Only Orders needs the `Services` block in Phase 1. Set `Otel.Endpoint` to `""` if Jaeger isn't running.)*

### 1.7.1 `Program.cs`

```csharp
using System.Diagnostics;
using System.Threading.RateLimiting;
using LankaMart.BuildingBlocks;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("gateway");
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformRedis(cfg);           // used for the logout blacklist
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1_000_000);   // 1 MB: JSON APIs never need more (uploads bypass the gateway in Phase 3)

// ---- CORS: which browser origins may call us. (In dev the Vite proxy makes this moot, but Flutter-web/other origins need it.) ----
var origins = cfg.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders("X-Request-Id").SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

// ---- Rate limiting: a global safety net + a strict policy for login/OTP ----
static string Client(HttpContext c) => c.Request.Headers["CF-Connecting-IP"].FirstOrDefault()   // behind Cloudflare (Phase 6)
                                       ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetFixedWindowLimiter(Client(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("auth", c =>
        RateLimitPartition.GetFixedWindowLimiter(Client(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddReverseProxy().LoadFromConfig(cfg.GetSection("ReverseProxy"));

var app = builder.Build();
app.UsePlatformDefaults();

app.Use(async (ctx, next) =>       // security headers + a request id the client can quote when reporting problems
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Request-Id"] = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
    await next();
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();

// Logout blacklist: a logged-out access token is dead immediately (not in up-to-15-minutes).
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true && ctx.User.FindFirst("jti")?.Value is { } jti)
    {
        var redis = ctx.RequestServices.GetRequiredService<IConnectionMultiplexer>();
        if (await redis.GetDatabase().KeyExistsAsync($"bl:{jti}")) { ctx.Response.StatusCode = 401; return; }
    }
    await next();
});

app.UseAuthorization();
app.MapReverseProxy();
app.Run();

public partial class Program;
```

### 1.7.2 Route table — `appsettings.json` (gateway)

🧠 **How to read it:** a route = *match* (path/method) + *cluster* (where to send) + *policies*. `"AuthorizationPolicy": "anonymous"` means *the gateway doesn't demand a token* (the service itself may still). Routes **without** that line require a valid JWT (our fallback policy). There is **no route** for `/api/v1/internal/**` — so the gateway returns 404 for it. That absence *is* the protection.

```json
{
  "AllowedHosts": "*",
  "Cors": { "Origins": [ "http://localhost:5173" ] },
  "ReverseProxy": {
    "Routes": {
      "auth-me":         { "ClusterId": "identity",  "CorsPolicy": "default", "Match": { "Path": "/api/v1/auth/me" } },
      "auth-logout":     { "ClusterId": "identity",  "CorsPolicy": "default", "Match": { "Path": "/api/v1/auth/logout" } },
      "auth-public":     { "ClusterId": "identity",  "AuthorizationPolicy": "anonymous", "RateLimiterPolicy": "auth", "CorsPolicy": "default",
                           "Match": { "Path": "/api/v1/auth/{**rest}" } },

      "products-public": { "ClusterId": "catalog",   "AuthorizationPolicy": "anonymous", "CorsPolicy": "default", "Match": { "Path": "/api/v1/products/{**rest}",   "Methods": [ "GET" ] } },
      "categories":      { "ClusterId": "catalog",   "AuthorizationPolicy": "anonymous", "CorsPolicy": "default", "Match": { "Path": "/api/v1/categories/{**rest}", "Methods": [ "GET" ] } },
      "seller-products": { "ClusterId": "catalog",   "CorsPolicy": "default", "Match": { "Path": "/api/v1/seller/products/{**rest}" } },

      "stock-public":    { "ClusterId": "inventory", "AuthorizationPolicy": "anonymous", "CorsPolicy": "default", "Match": { "Path": "/api/v1/inventory/{**rest}", "Methods": [ "GET" ] } },
      "seller-inventory":{ "ClusterId": "inventory", "CorsPolicy": "default", "Match": { "Path": "/api/v1/seller/inventory/{**rest}" } },

      "customers":       { "ClusterId": "users",     "CorsPolicy": "default", "Match": { "Path": "/api/v1/customers/{**rest}" } },
      "sellers":         { "ClusterId": "users",     "CorsPolicy": "default", "Match": { "Path": "/api/v1/sellers/{**rest}" } },
      "admin-sellers":   { "ClusterId": "users",     "CorsPolicy": "default", "Match": { "Path": "/api/v1/admin/sellers/{**rest}" } },

      "cart":            { "ClusterId": "orders",    "AuthorizationPolicy": "anonymous", "CorsPolicy": "default", "Match": { "Path": "/api/v1/cart/{**rest}" } },
      "orders":          { "ClusterId": "orders",    "CorsPolicy": "default", "Match": { "Path": "/api/v1/orders/{**rest}" } },

      "payments":        { "ClusterId": "payments",  "CorsPolicy": "default", "Match": { "Path": "/api/v1/payments/{**rest}" } },
      "webhooks":        { "ClusterId": "payments",  "AuthorizationPolicy": "anonymous", "Match": { "Path": "/api/v1/webhooks/{**rest}", "Methods": [ "POST" ] } },

      "recommendations": { "ClusterId": "recommender","AuthorizationPolicy": "anonymous", "CorsPolicy": "default", "Match": { "Path": "/api/v1/recommendations/{**rest}", "Methods": [ "GET" ] } }
    },
    "Clusters": {
      "identity":    { "Destinations": { "d1": { "Address": "http://localhost:5101/" } } },
      "users":       { "Destinations": { "d1": { "Address": "http://localhost:5102/" } } },
      "catalog":     { "Destinations": { "d1": { "Address": "http://localhost:5103/" } } },
      "inventory":   { "Destinations": { "d1": { "Address": "http://localhost:5104/" } } },
      "orders":      { "Destinations": { "d1": { "Address": "http://localhost:5105/" } } },
      "payments":    { "Destinations": { "d1": { "Address": "http://localhost:5106/" } } },
      "recommender": { "Destinations": { "d1": { "Address": "http://localhost:5110/" } } }
    }
  }
}
```

🧠 *Why three auth routes?* ASP.NET routing prefers **literal** paths over catch-alls, so `/api/v1/auth/me` and `/auth/logout` match their own routes (token required, normal rate limit) while everything else under `/auth/*` (register, login, OTP, refresh) falls into the catch-all: anonymous at the gateway but **strictly rate-limited (10/min per IP)** — that's your brute-force brake.

`appsettings.Development.json` (gateway) — **dev-only** routes so React can read OTPs and advance orders (these routes do not exist in production config):

```json
{
  "ReverseProxy": {
    "Routes": {
      "dev-otp":    { "ClusterId": "identity", "AuthorizationPolicy": "anonymous", "Match": { "Path": "/api/v1/dev/otp/{**rest}" } },
      "dev-orders": { "ClusterId": "orders",   "Match": { "Path": "/api/v1/dev/orders/{**rest}" } },
      "dev-mock":   { "ClusterId": "payments", "AuthorizationPolicy": "anonymous", "Match": { "Path": "/api/v1/dev/mock/{**rest}" } }
    }
  },
  "Auth": { "Authority": "http://localhost:5101", "Issuer": "lankamart-identity", "Audience": "lankamart-api", "RequireHttps": false },
  "Redis": { "ConnectionString": "localhost:6379" },
  "Otel": { "Endpoint": "http://localhost:4317" }
}
```
*(Configuration files merge: arrays and objects from `appsettings.Development.json` are added to `appsettings.json`.)*

✅ **Check (after Identity is running)**
```bash
curl -i http://localhost:5100/api/v1/auth/me                       # 401  (gateway demands a token on this route)
curl -s -X POST http://localhost:5100/api/v1/auth/login -H "Content-Type: application/json" -d '{"email":"admin@lankamart.local","password":"Admin#12345678"}'
curl -s http://localhost:5100/api/v1/auth/me -H "Authorization: Bearer <token>"     # 200 JSON
curl -i http://localhost:5100/api/v1/internal/inventory/reserve    # 404  ← internal routes unreachable
for i in $(seq 1 12); do curl -s -o /dev/null -w "%{http_code} " -X POST http://localhost:5100/api/v1/auth/login -H "Content-Type: application/json" -d '{"email":"x@y.z","password":"wrong"}'; done   # …401 401 … then 429 429
# logout test: login → call /me (200) → POST /auth/logout → call /me with the SAME access token → 401 immediately
```

⚠️ **Pitfalls:** a route returning **404 for a real path** usually means a typo in `Match.Path` · **CORS errors in the browser console** → your React origin isn't in `Cors:Origins` (or you're not using the Vite proxy) · rate limiting makes *your own* testing return 429 — wait a minute or temporarily raise the limit in dev.

Commit: `feat(gateway): real routes, rate limiting, jwt, blacklist`.

---

## 1.8 User service

**Port 5102 · schema `users` · login `users_svc`.**
**Mission:** "what we know about you" — customer profiles + address book, and the **seller onboarding state machine** with KYC form data. (Identity answers *who you are*; Users answers *what we know about you*.)

**Endpoints**

| Method & path | Who | Purpose |
|---|---|---|
| `GET/PUT /api/v1/customers/me` | customer | profile |
| `GET/POST /api/v1/customers/me/addresses` · `PUT/DELETE …/{id}` | customer | address book |
| `GET /api/v1/sellers/me` | seller | my seller profile + status |
| `POST /api/v1/sellers/kyc` | seller (verified) | submit/resubmit KYC data |
| `GET /api/v1/admin/sellers?status=&page=` · `GET …/{id}` | seller_manager, super_admin | review queue |
| `POST /api/v1/admin/sellers/{id}/start-review · approve · reject · suspend · reinstate` | seller_manager, super_admin | decisions |

### 1.8.0 Two small additions to BuildingBlocks first

`src/shared/LankaMart.BuildingBlocks/Domain/Reference.cs`:
```csharp
namespace LankaMart.BuildingBlocks;

public static class SriLanka
{
    public static readonly string[] Districts =
    [
        "Ampara","Anuradhapura","Badulla","Batticaloa","Colombo","Galle","Gampaha","Hambantota","Jaffna","Kalutara","Kandy","Kegalle","Kilinochchi",
        "Kurunegala","Mannar","Matale","Matara","Monaragala","Mullaitivu","Nuwara Eliya","Polonnaruwa","Puttalam","Ratnapura","Trincomalee","Vavuniya"
    ];
}

public static class Lang
{
    public static readonly string[] Supported = ["en", "si", "ta"];
    public static string Normalize(string? l) => l is not null && Supported.Contains(l.ToLowerInvariant()) ? l.ToLowerInvariant() : "en";
}

public static class Like
{
    /// <summary>Escape user text for use inside ILIKE with escape char '\'. Without this a search for "%" matches everything.</summary>
    public static string Escape(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
```

### 1.8.1 Packages, config, GlobalUsings
Same four EF packages as Identity (§1.6.1) + the `GlobalUsings.cs` from §1.5. `appsettings.Development.json` = template from §1.7.0 with `Username=users_svc;Password=users_dev_pw`.

### 1.8.2 Domain

`Domain/Entities.cs`
```csharp
namespace LankaMart.Users.Api.Domain;

public class CustomerProfile
{
    public Guid UserId { get; set; }                  // same id as the Identity user
    public string Email { get; set; } = ""; public string Phone { get; set; } = "";
    public string FullName { get; set; } = ""; public string PreferredLanguage { get; set; } = "en";
    public DateTimeOffset CreatedAt { get; set; }
    public List<Address> Addresses { get; set; } = [];
}

public class Address
{
    public Guid Id { get; set; } public Guid UserId { get; set; }
    public string Label { get; set; } = "Home"; public string RecipientName { get; set; } = ""; public string Phone { get; set; } = "";
    public string Line1 { get; set; } = ""; public string? Line2 { get; set; }
    public string City { get; set; } = ""; public string District { get; set; } = ""; public string? PostalCode { get; set; }
    public bool IsDefault { get; set; } public DateTimeOffset CreatedAt { get; set; }
}

public enum SellerStatus { Registered, KycSubmitted, UnderReview, Active, Rejected, Suspended }

public sealed record KycData(string BusinessName, string BusinessType, string? BrNumber, string GoodsCategory, string NicNumber,
                             string AddressLine, string District, string BankName, string BankBranch, string BankAccountName, string BankAccountNumber);

/// <summary>The Seller Onboarding State Machine (architecture doc §4.2):
///   Registered → KycSubmitted → UnderReview → Active ⇄ Suspended ;   KycSubmitted/UnderReview → Rejected → (resubmit) KycSubmitted
/// Why a state machine and not a bool "approved"? Suspended sellers keep their history but can't sell; a boolean can't say that.</summary>
public class SellerProfile
{
    public Guid UserId { get; private set; }          // == seller id platform-wide
    public string Email { get; private set; } = ""; public string Phone { get; private set; } = ""; public string DisplayName { get; private set; } = "";
    public SellerStatus Status { get; private set; } = SellerStatus.Registered;

    // KYC form data (document uploads arrive in Phase 3 with the Media service)
    public string? BusinessName { get; private set; } public string? BusinessType { get; private set; } public string? BrNumber { get; private set; }
    public string? GoodsCategory { get; private set; } public string? NicNumber { get; private set; }       // TODO Phase 6: encrypt at rest (AES-GCM, key in Vault)
    public string? AddressLine { get; private set; } public string? District { get; private set; }
    public string? BankName { get; private set; } public string? BankBranch { get; private set; }
    public string? BankAccountName { get; private set; } public string? BankAccountNumber { get; private set; }   // TODO Phase 6: encrypt

    public int KycAttempts { get; private set; }
    public string? RejectionReasonCode { get; private set; } public string? RejectionNote { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; } public Guid? DecidedBy { get; private set; }

    public static SellerProfile Create(Guid userId, string name, string email, string phone, DateTimeOffset now) =>
        new() { UserId = userId, DisplayName = name, Email = email, Phone = phone, CreatedAt = now };

    private void Require(string action, params SellerStatus[] allowed)
    {
        if (!allowed.Contains(Status)) throw new BusinessRuleException("INVALID_SELLER_STATE", $"Cannot {action} while seller is {Status}.");
    }

    public void SubmitKyc(KycData d, DateTimeOffset now)
    {
        Require("submit KYC", SellerStatus.Registered, SellerStatus.Rejected);          // resubmission allowed only after rejection (unlimited attempts, counted)
        BusinessName = d.BusinessName; BusinessType = d.BusinessType; BrNumber = d.BrNumber; GoodsCategory = d.GoodsCategory;
        NicNumber = d.NicNumber; AddressLine = d.AddressLine; District = d.District;
        BankName = d.BankName; BankBranch = d.BankBranch; BankAccountName = d.BankAccountName; BankAccountNumber = d.BankAccountNumber;
        KycAttempts++; SubmittedAt = now; RejectionReasonCode = RejectionNote = null; Status = SellerStatus.KycSubmitted;
    }
    public void StartReview(Guid by) { Require("start review", SellerStatus.KycSubmitted); Status = SellerStatus.UnderReview; DecidedBy = by; }
    public void Approve(Guid by, DateTimeOffset now) { Require("approve", SellerStatus.KycSubmitted, SellerStatus.UnderReview); Status = SellerStatus.Active; Decide(by, now); }
    public void Reject(Guid by, string code, string? note, DateTimeOffset now)
    { Require("reject", SellerStatus.KycSubmitted, SellerStatus.UnderReview); Status = SellerStatus.Rejected; RejectionReasonCode = code; RejectionNote = note; Decide(by, now); }
    public void Suspend(Guid by, DateTimeOffset now) { Require("suspend", SellerStatus.Active); Status = SellerStatus.Suspended; Decide(by, now); }
    public void Reinstate(Guid by, DateTimeOffset now) { Require("reinstate", SellerStatus.Suspended); Status = SellerStatus.Active; Decide(by, now); }
    private void Decide(Guid by, DateTimeOffset now) { DecidedBy = by; DecidedAt = now; }
}
```

`Data/UsersDb.cs`
```csharp
using LankaMart.Users.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace LankaMart.Users.Api.Data;

public class UsersDb(DbContextOptions<UsersDb> options) : DbContext(options)
{
    public DbSet<CustomerProfile> Customers => Set<CustomerProfile>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<SellerProfile> Sellers => Set<SellerProfile>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("users");
        b.Entity<CustomerProfile>(e => { e.HasKey(x => x.UserId); e.HasMany(x => x.Addresses).WithOne().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<Address>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.UserId); });
        b.Entity<SellerProfile>(e => { e.HasKey(x => x.UserId); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30); e.HasIndex(x => x.Status); });
    }
}
```

### 1.8.3 The consumer — how a profile gets created

`Consumers/UserRegisteredConsumer.cs`
```csharp
using LankaMart.Users.Api.Data;
using LankaMart.Users.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Users.Api.Consumers;

public sealed class UserRegisteredConsumer(UsersDb db, ILogger<UserRegisteredConsumer> log) : IConsumer<UserRegistered>
{
    public async Task Consume(ConsumeContext<UserRegistered> ctx)
    {
        var m = ctx.Message;
        // IDEMPOTENT: RabbitMQ may deliver twice. "If it already exists, do nothing" makes the second delivery harmless.
        if (m.Role == Roles.Seller)
        {
            if (await db.Sellers.AnyAsync(s => s.UserId == m.UserId)) return;
            db.Sellers.Add(SellerProfile.Create(m.UserId, m.FullName, m.Email, m.Phone, m.OccurredAt));
        }
        else
        {
            if (await db.Customers.AnyAsync(c => c.UserId == m.UserId)) return;
            db.Customers.Add(new CustomerProfile { UserId = m.UserId, Email = m.Email, Phone = m.Phone, FullName = m.FullName, PreferredLanguage = m.Language, CreatedAt = m.OccurredAt });
        }
        await db.SaveChangesAsync();      // a racing duplicate hits the PK → exception → MassTransit retries → next attempt sees "exists" and returns
        log.LogInformation("Profile created for {UserId} ({Role})", m.UserId, m.Role);
    }
}
```

### 1.8.4 Customer endpoints

`Features/CustomerEndpoints.cs`
```csharp
using FluentValidation;
using LankaMart.Users.Api.Data;
using LankaMart.Users.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Users.Api.Features;

public sealed record UpdateProfileRequest(string FullName, string PreferredLanguage);
public sealed record AddressRequest(string Label, string RecipientName, string Phone, string Line1, string? Line2, string City, string District, string? PostalCode, bool IsDefault);
public sealed record AddressDto(Guid Id, string Label, string RecipientName, string Phone, string Line1, string? Line2, string City, string District, string? PostalCode, bool IsDefault);

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileRequest>
{ public UpdateProfileValidator() { RuleFor(x => x.FullName).Length(2, 100); RuleFor(x => x.PreferredLanguage).Must(l => Lang.Supported.Contains(l)); } }

public sealed class AddressValidator : AbstractValidator<AddressRequest>
{
    public AddressValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(30);
        RuleFor(x => x.RecipientName).Length(2, 100);
        RuleFor(x => x.Phone).Must(p => PhoneNumber.TryNormalizeLk(p, out _)).WithMessage("Enter a valid Sri Lankan mobile number.");
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Line2).MaximumLength(150);
        RuleFor(x => x.City).NotEmpty().MaximumLength(80);
        RuleFor(x => x.District).Must(d => SriLanka.Districts.Contains(d)).WithMessage("Unknown district.");
        RuleFor(x => x.PostalCode).Matches(@"^\d{5}$").When(x => !string.IsNullOrEmpty(x.PostalCode));
    }
}

public static class CustomerEndpoints
{
    private static AddressDto ToDto(Address a) => new(a.Id, a.Label, a.RecipientName, a.Phone, a.Line1, a.Line2, a.City, a.District, a.PostalCode, a.IsDefault);

    public static void MapCustomers(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/customers/me").WithTags("Customers").RequireAuthorization(Policies.Customer);

        // NOTE: no {id} in these URLs. "me" is derived from the TOKEN → there is nothing to tamper with → IDOR-proof by construction.
        g.MapGet("/", async (HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var p = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == c.User.UserId(), ct)
                    ?? throw new ConflictException("PROFILE_NOT_READY", "Your profile is being set up. Try again in a moment.");   // eventual consistency, honestly surfaced
            return Results.Ok(new { p.UserId, p.Email, p.Phone, p.FullName, p.PreferredLanguage });
        });

        g.MapPut("/", async (UpdateProfileRequest r, HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var p = await db.Customers.SingleOrDefaultAsync(x => x.UserId == c.User.UserId(), ct) ?? throw new NotFoundException();
            p.FullName = r.FullName.Trim(); p.PreferredLanguage = r.PreferredLanguage;       // only whitelisted fields are copied (mass-assignment defence)
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithValidation<UpdateProfileRequest>();

        g.MapGet("/addresses", async (HttpContext c, UsersDb db, CancellationToken ct) =>
            Results.Ok((await db.Addresses.AsNoTracking().Where(a => a.UserId == c.User.UserId()).OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt).ToListAsync(ct)).Select(ToDto)));

        g.MapPost("/addresses", async (AddressRequest r, HttpContext c, UsersDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            if (!await db.Customers.AnyAsync(x => x.UserId == uid, ct)) throw new ConflictException("PROFILE_NOT_READY", "Your profile is being set up. Try again in a moment.");
            if (await db.Addresses.CountAsync(a => a.UserId == uid, ct) >= 10) throw new BusinessRuleException("ADDRESS_LIMIT", "You can save up to 10 addresses.");
            PhoneNumber.TryNormalizeLk(r.Phone, out var phone);
            var isFirst = !await db.Addresses.AnyAsync(a => a.UserId == uid, ct);
            var a = new Address { Id = Guid.CreateVersion7(), UserId = uid, Label = r.Label, RecipientName = r.RecipientName.Trim(), Phone = phone, Line1 = r.Line1.Trim(), Line2 = r.Line2?.Trim(),
                                  City = r.City.Trim(), District = r.District, PostalCode = r.PostalCode, IsDefault = r.IsDefault || isFirst, CreatedAt = clock.GetUtcNow() };
            if (a.IsDefault) await db.Addresses.Where(x => x.UserId == uid && x.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);
            db.Addresses.Add(a);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/customers/me/addresses/{a.Id}", ToDto(a));
        }).WithValidation<AddressRequest>();

        g.MapPut("/addresses/{id:guid}", async (Guid id, AddressRequest r, HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var a = await db.Addresses.SingleOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct) ?? throw new NotFoundException();   // ← the IDOR defence: filter by id AND owner; 404 if not mine
            PhoneNumber.TryNormalizeLk(r.Phone, out var phone);
            a.Label = r.Label; a.RecipientName = r.RecipientName.Trim(); a.Phone = phone; a.Line1 = r.Line1.Trim(); a.Line2 = r.Line2?.Trim(); a.City = r.City.Trim(); a.District = r.District; a.PostalCode = r.PostalCode;
            if (r.IsDefault && !a.IsDefault) { await db.Addresses.Where(x => x.UserId == uid && x.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct); a.IsDefault = true; }
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(a));
        }).WithValidation<AddressRequest>();

        g.MapDelete("/addresses/{id:guid}", async (Guid id, HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var n = await db.Addresses.Where(x => x.Id == id && x.UserId == c.User.UserId()).ExecuteDeleteAsync(ct);
            return n == 0 ? throw new NotFoundException() : Results.NoContent();
        });
    }
}
```

### 1.8.5 Seller & admin endpoints

`Features/SellerEndpoints.cs`
```csharp
using System.Text.RegularExpressions;
using FluentValidation;
using LankaMart.Users.Api.Data;
using LankaMart.Users.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Users.Api.Features;

public sealed record KycRequest(string BusinessName, string BusinessType, string? BrNumber, string GoodsCategory, string NicNumber,
                                string AddressLine, string District, string BankName, string BankBranch, string BankAccountName, string BankAccountNumber);
public sealed record RejectRequest(string ReasonCode, string? Note);
public sealed record SuspendRequest(string Reason);

public sealed class KycValidator : AbstractValidator<KycRequest>
{
    public static readonly string[] RejectCodes = ["NIC_MISMATCH", "ADDRESS_PROOF_INVALID", "BANK_MISMATCH", "INCOMPLETE", "OTHER"];
    public KycValidator()
    {
        RuleFor(x => x.BusinessName).Length(2, 120);
        RuleFor(x => x.BusinessType).Must(t => t is "individual" or "registered");
        RuleFor(x => x.BrNumber).NotEmpty().When(x => x.BusinessType == "registered").WithMessage("BR number is required for registered businesses.");
        RuleFor(x => x.GoodsCategory).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NicNumber).Matches(@"^(\d{9}[VvXx]|\d{12})$").WithMessage("Enter a valid Sri Lankan NIC (old 9 digits+V/X or new 12 digits).");
        RuleFor(x => x.AddressLine).NotEmpty().MaximumLength(200);
        RuleFor(x => x.District).Must(d => SriLanka.Districts.Contains(d));
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(80); RuleFor(x => x.BankBranch).NotEmpty().MaximumLength(80);
        RuleFor(x => x.BankAccountName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.BankAccountNumber).Matches(@"^\d{6,20}$");
    }
}
public sealed class RejectValidator : AbstractValidator<RejectRequest>
{ public RejectValidator() { RuleFor(x => x.ReasonCode).Must(c => KycValidator.RejectCodes.Contains(c)); RuleFor(x => x.Note).MaximumLength(500); } }
public sealed class SuspendValidator : AbstractValidator<SuspendRequest>
{ public SuspendValidator() { RuleFor(x => x.Reason).NotEmpty().MaximumLength(500); } }   // a reason is MANDATORY for suspensions (audit requirement)

public static class SellerEndpoints
{
    private static string Mask(string? s) => string.IsNullOrEmpty(s) ? "" : new string('•', Math.Max(0, s.Length - 4)) + s[^4..];

    public static void MapSellers(this WebApplication app)
    {
        var me = app.MapGroup("/api/v1/sellers").WithTags("Sellers").RequireAuthorization(Policies.Seller);

        me.MapGet("/me", async (HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var s = await db.Sellers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == c.User.UserId(), ct)
                    ?? throw new ConflictException("PROFILE_NOT_READY", "Your seller profile is being set up. Try again in a moment.");
            return Results.Ok(new { s.UserId, status = s.Status.ToString(), s.DisplayName, s.BusinessName, s.KycAttempts, s.RejectionReasonCode, s.RejectionNote, s.SubmittedAt, s.DecidedAt });
        });

        me.MapPost("/kyc", async (KycRequest r, HttpContext c, UsersDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            if (!c.User.IsFullyVerified()) throw new ForbiddenException("VERIFICATION_REQUIRED", "Verify your email and phone before submitting KYC.");   // System Scenarios §3.1 step 1
            var s = await db.Sellers.SingleOrDefaultAsync(x => x.UserId == c.User.UserId(), ct) ?? throw new ConflictException("PROFILE_NOT_READY", "Try again in a moment.");
            var now = clock.GetUtcNow();
            s.SubmitKyc(new KycData(r.BusinessName.Trim(), r.BusinessType, r.BrNumber?.Trim(), r.GoodsCategory.Trim(), r.NicNumber.ToUpperInvariant(),
                                    r.AddressLine.Trim(), r.District, r.BankName.Trim(), r.BankBranch.Trim(), r.BankAccountName.Trim(), r.BankAccountNumber), now);
            await db.SaveChangesAsync(ct);
            await bus.Publish(new SellerKycSubmitted(s.UserId, s.BusinessName!, now), ct);
            return Results.Accepted(value: new { status = s.Status.ToString() });
        }).WithValidation<KycRequest>();

        // ───── Admin side: Seller Manager (and Super Admin) ─────
        var admin = app.MapGroup("/api/v1/admin/sellers").WithTags("Admin: Sellers").RequireAuthorization(Policies.SellerAdmin);

        admin.MapGet("/", async (UsersDb db, string? status, int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = Paging.Normalize(page, pageSize);
            var q = db.Sellers.AsNoTracking().AsQueryable();
            if (Enum.TryParse<SellerStatus>(status, true, out var st)) q = q.Where(x => x.Status == st);
            var total = await q.LongCountAsync(ct);
            var items = await q.OrderBy(x => x.SubmittedAt ?? x.CreatedAt).Skip((p - 1) * size).Take(size)
                .Select(x => new { x.UserId, status = x.Status.ToString(), x.DisplayName, x.BusinessName, x.District, x.KycAttempts, x.SubmittedAt }).ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(items.Cast<object>().ToList(), p, size, total));
        });

        admin.MapGet("/{id:guid}", async (Guid id, UsersDb db, ILogger<Program> log, HttpContext c, CancellationToken ct) =>
        {
            var s = await db.Sellers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == id, ct) ?? throw new NotFoundException();
            log.LogInformation("AUDIT pii_view admin={Admin} seller={Seller}", c.User.UserId(), id);     // PII access is itself audited (System Scenarios §5.3)
            return Results.Ok(new { s.UserId, status = s.Status.ToString(), s.DisplayName, s.Email, s.Phone, s.BusinessName, s.BusinessType, s.BrNumber, s.GoodsCategory,
                                    nic = Mask(s.NicNumber), s.AddressLine, s.District, s.BankName, s.BankBranch, s.BankAccountName, bankAccount = Mask(s.BankAccountNumber),
                                    s.KycAttempts, s.RejectionReasonCode, s.RejectionNote, s.SubmittedAt, s.DecidedAt, s.DecidedBy });
        });

        async Task<SellerProfile> Load(UsersDb db, Guid id, CancellationToken ct) =>
            await db.Sellers.SingleOrDefaultAsync(x => x.UserId == id, ct) ?? throw new NotFoundException();

        admin.MapPost("/{id:guid}/start-review", async (Guid id, UsersDb db, HttpContext c, CancellationToken ct) =>
        { var s = await Load(db, id, ct); s.StartReview(c.User.UserId()); await db.SaveChangesAsync(ct); return Results.NoContent(); });

        admin.MapPost("/{id:guid}/approve", async (Guid id, UsersDb db, IPublishEndpoint bus, TimeProvider clock, ILogger<Program> log, HttpContext c, CancellationToken ct) =>
        {
            var s = await Load(db, id, ct); var now = clock.GetUtcNow();
            s.Approve(c.User.UserId(), now); await db.SaveChangesAsync(ct);
            log.LogInformation("AUDIT seller.approve admin={Admin} seller={Seller}", c.User.UserId(), id);
            await bus.Publish(new SellerApproved(s.UserId, s.BusinessName ?? s.DisplayName, now), ct);
            return Results.NoContent();
        });

        admin.MapPost("/{id:guid}/reject", async (Guid id, RejectRequest r, UsersDb db, IPublishEndpoint bus, TimeProvider clock, ILogger<Program> log, HttpContext c, CancellationToken ct) =>
        {
            var s = await Load(db, id, ct); var now = clock.GetUtcNow();
            s.Reject(c.User.UserId(), r.ReasonCode, r.Note, now); await db.SaveChangesAsync(ct);
            log.LogInformation("AUDIT seller.reject admin={Admin} seller={Seller} code={Code}", c.User.UserId(), id, r.ReasonCode);
            await bus.Publish(new SellerRejected(s.UserId, r.ReasonCode, now), ct);
            return Results.NoContent();
        }).WithValidation<RejectRequest>();

        admin.MapPost("/{id:guid}/suspend", async (Guid id, SuspendRequest r, UsersDb db, IPublishEndpoint bus, TimeProvider clock, ILogger<Program> log, HttpContext c, CancellationToken ct) =>
        {
            var s = await Load(db, id, ct); var now = clock.GetUtcNow();
            s.Suspend(c.User.UserId(), now); await db.SaveChangesAsync(ct);
            log.LogInformation("AUDIT seller.suspend admin={Admin} seller={Seller} reason={Reason}", c.User.UserId(), id, r.Reason);
            await bus.Publish(new SellerSuspended(s.UserId, r.Reason, now), ct);
            return Results.NoContent();
        }).WithValidation<SuspendRequest>();

        admin.MapPost("/{id:guid}/reinstate", async (Guid id, UsersDb db, IPublishEndpoint bus, TimeProvider clock, HttpContext c, CancellationToken ct) =>
        {
            var s = await Load(db, id, ct); var now = clock.GetUtcNow();
            s.Reinstate(c.User.UserId(), now); await db.SaveChangesAsync(ct);
            await bus.Publish(new SellerReinstated(s.UserId, now), ct);
            return Results.NoContent();
        });
    }
}
```
🧠 *The `AUDIT …` log lines are a stand-in.* In Phase 6 the Security & Audit service consumes events and writes a tamper-evident log. Writing structured `AUDIT` lines now gives you the habit and the data shape.

### 1.8.6 `Program.cs`

```csharp
using FluentValidation;
using LankaMart.Users.Api.Data;
using LankaMart.Users.Api.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("users");

builder.Services.AddDbContext<UsersDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "users"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "users", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
await app.MigrateDatabaseAsync<UsersDb>();
app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapCustomers();
app.MapSellers();
app.Run();

public partial class Program;
```

```bash
dotnet ef migrations add Initial --project src/services/users/LankaMart.Users.Api --output-dir Data/Migrations
```

✅ **Check (Identity + Users + Gateway + infra running)**
1. Register a customer through the **gateway** (`POST :5100/api/v1/auth/register`) → within a second, **RabbitMQ UI → Queues** shows `users-user-registered` and a row appears in `users.customer_profiles` (pgAdmin). *You just watched an event cross a service boundary.*
2. Verify both OTPs, login, `GET /api/v1/customers/me` → 200 profile. Create an address; try `GET/PUT/DELETE` of an address id belonging to **another** user → **404**.
3. Register a seller (role `seller`), verify, login, `GET /api/v1/sellers/me` → `Registered`. Try `POST /sellers/kyc` **before** verifying → 403 `VERIFICATION_REQUIRED`. Verify, submit KYC → `KycSubmitted`.
4. Login as `sellermgr@lankamart.local` → `GET /admin/sellers?status=KycSubmitted` → see the seller; `POST …/approve` → 204.
5. As the seller: `GET /sellers/me` → `Active`. As a **customer** call an admin endpoint → **403**. Approve the same seller twice → 422 `INVALID_SELLER_STATE`.
6. **Idempotency test:** in RabbitMQ UI, re-publish a `UserRegistered` message → no duplicate profile.

⚠️ **Pitfalls:** `PROFILE_NOT_READY` forever → the consumer isn't running (is `AddConsumers(typeof(Program).Assembly)` there? Check the Users log for MassTransit errors; check RabbitMQ credentials) · messages piling up in a `…_error` queue → a consumer threw; open the message in the RabbitMQ UI to read the exception header.

Commit: `feat(users): profiles, addresses, seller KYC state machine`.

---

## 1.9 Catalog service

**Port 5103 · schema `catalog` · login `catalog_svc`.**
**Mission:** *what is for sale.* The most **read-heavy** service. Products with translations (schema ready for si/ta; Phase 1 uses `en`), variants (SKU/price/attributes), images (URLs), categories, search, and an internal price-lookup used by Orders.

**Endpoints**

| Method & path | Who | Purpose |
|---|---|---|
| `GET /api/v1/products?query=&category=&min=&max=&sort=&lang=&page=&page_size=` | anyone | search/list |
| `GET /api/v1/products/{slug}?lang=` | anyone | product detail |
| `GET /api/v1/categories?lang=` | anyone | category tree |
| `POST/PUT /api/v1/seller/products[/{id}]` | **approved** seller | create / edit own product |
| `POST /api/v1/seller/products/{id}/variants` · `PUT …/variants/{variantId}` | approved seller | add / edit variants |
| `POST /api/v1/seller/products/{id}/publish` · `/archive` | approved seller | go live / hide |
| `GET /api/v1/seller/products` | seller | my products |
| `POST /api/v1/internal/variants/lookup` | **Orders (internal key)** | authoritative price/name/seller |

### 1.9.1 Domain & DB

Packages/config as Users (login `catalog_svc`).

`Domain/Entities.cs`
```csharp
namespace LankaMart.Catalog.Api.Domain;

public enum ProductStatus { Draft, PendingModeration, Active, Rejected, Archived }   // PendingModeration/Rejected are used from Phase 3

public class Category
{
    public Guid Id { get; set; } public Guid? ParentId { get; set; } public string Slug { get; set; } = ""; public int SortOrder { get; set; }
    public List<CategoryTranslation> Translations { get; set; } = [];
}
public class CategoryTranslation { public Guid CategoryId { get; set; } public string Lang { get; set; } = "en"; public string Name { get; set; } = ""; }

public class Product
{
    public Guid Id { get; set; } public Guid SellerId { get; set; } public Guid CategoryId { get; set; } public string? Brand { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public double RatingAvg { get; set; } public int RatingCount { get; set; }                  // denormalised; filled by reviews in Phase 3
    public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; }
    public List<ProductTranslation> Translations { get; set; } = [];
    public List<ProductVariant> Variants { get; set; } = [];
    public List<ProductImage> Images { get; set; } = [];
}
// One row per language → adding Tamil later needs NO migration, and a missing translation falls back to English.
public class ProductTranslation
{
    public Guid ProductId { get; set; } public string Lang { get; set; } = "en";
    public string Name { get; set; } = ""; public string Description { get; set; } = ""; public string Slug { get; set; } = "";
}
public class ProductVariant
{
    public Guid Id { get; set; } public Guid ProductId { get; set; } public Product Product { get; set; } = null!;
    public Guid SellerId { get; set; }                        // denormalised so (seller, sku) can be unique
    public string Sku { get; set; } = "";
    public string AttributesJson { get; set; } = "{}";        // e.g. {"color":"Black","size":"42"} stored as jsonb
    public long PriceCents { get; set; } public long? CompareAtPriceCents { get; set; } public bool IsActive { get; set; } = true;
}
public class ProductImage
{
    public Guid Id { get; set; } public Guid ProductId { get; set; } public Guid? VariantId { get; set; }
    public string Url { get; set; } = ""; public string? Alt { get; set; } public int SortOrder { get; set; }
}
/// <summary>Catalog's local copy of "which sellers may sell", built from SellerApproved/Suspended events — so it never has to call the Users service.</summary>
public class KnownSeller { public Guid SellerId { get; set; } public string Status { get; set; } = "Registered"; public DateTimeOffset UpdatedAt { get; set; } }
```

`Data/CatalogDb.cs`
```csharp
using LankaMart.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace LankaMart.Catalog.Api.Data;

public class CatalogDb(DbContextOptions<CatalogDb> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryTranslation> CategoryTranslations => Set<CategoryTranslation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductTranslation> ProductTranslations => Set<ProductTranslation>();
    public DbSet<ProductVariant> Variants => Set<ProductVariant>();
    public DbSet<ProductImage> Images => Set<ProductImage>();
    public DbSet<KnownSeller> KnownSellers => Set<KnownSeller>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("catalog");
        b.Entity<Category>(e => { e.HasIndex(x => x.Slug).IsUnique(); e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.CategoryId); });
        b.Entity<CategoryTranslation>().HasKey(x => new { x.CategoryId, x.Lang });

        b.Entity<Product>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.Status, x.CategoryId }); e.HasIndex(x => x.SellerId);
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Variants).WithOne(v => v.Product).HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ProductTranslation>(e => { e.HasKey(x => new { x.ProductId, x.Lang }); e.HasIndex(x => new { x.Lang, x.Slug }).IsUnique(); });
        b.Entity<ProductVariant>(e => { e.HasIndex(x => new { x.SellerId, x.Sku }).IsUnique(); e.Property(x => x.AttributesJson).HasColumnType("jsonb"); });
        b.Entity<KnownSeller>().HasKey(x => x.SellerId);
    }
}
```

Create the migration, then **add the trigram index by hand** in the generated `Up()` (this is what makes `ILIKE '%text%'` fast instead of scanning every row):

```bash
dotnet ef migrations add Initial --project src/services/catalog/LankaMart.Catalog.Api --output-dir Data/Migrations
```
```csharp
// at the END of Up() in the generated Initial migration:
migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_product_translations_name_trgm ON catalog.product_translations USING gin (name gin_trgm_ops);");
// at the END of Down():
migrationBuilder.Sql("DROP INDEX IF EXISTS catalog.ix_product_translations_name_trgm;");
```
🧠 *`pg_trgm` splits text into 3-letter chunks ("sho","hoe","oes") and indexes them, so "shoe" finds "Running Shoes" without a full scan. It's the Phase 1 stand-in for Meilisearch.*

### 1.9.2 DTOs, validation, slugs

`Features/Dtos.cs`
```csharp
using FluentValidation;
using LankaMart.Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace LankaMart.Catalog.Api.Features;

public sealed record TranslationDto(string Lang, string Name, string Description);
public sealed record VariantDto(string Sku, Dictionary<string, string>? Attributes, long PriceCents, long? CompareAtPriceCents);
public sealed record ImageDto(string Url, string? Alt);
public sealed record CreateProductRequest(Guid CategoryId, string? Brand, List<TranslationDto> Translations, List<VariantDto> Variants, List<ImageDto>? Images);
public sealed record UpdateProductRequest(Guid CategoryId, string? Brand, List<TranslationDto> Translations);
public sealed record UpdateVariantRequest(long PriceCents, long? CompareAtPriceCents, bool IsActive);

public sealed record ProductListItem(Guid Id, string Slug, string Name, string? ImageUrl, long PriceFromCents, double RatingAvg, int RatingCount, Guid SellerId);

public sealed class TranslationValidator : AbstractValidator<TranslationDto>
{ public TranslationValidator() { RuleFor(x => x.Lang).Must(l => Lang.Supported.Contains(l)); RuleFor(x => x.Name).Length(3, 200); RuleFor(x => x.Description).MaximumLength(5000); } }
public sealed class VariantValidator : AbstractValidator<VariantDto>
{
    public VariantValidator()
    {
        RuleFor(x => x.Sku).Matches(@"^[A-Za-z0-9._-]{2,40}$");
        RuleFor(x => x.PriceCents).GreaterThan(0).LessThan(1_000_000_000);           // < LKR 10,000,000
        RuleFor(x => x.CompareAtPriceCents).GreaterThan(x => x.PriceCents).When(x => x.CompareAtPriceCents is not null);
        RuleFor(x => x.Attributes).Must(a => a is null || (a.Count <= 10 && a.All(kv => kv.Key.Length <= 30 && kv.Value.Length <= 60)));
    }
}
public sealed class ImageValidator : AbstractValidator<ImageDto>
{   // only http(s) URLs: blocks javascript:/data: URLs that could become stored XSS when rendered in <img src>/<a href>
    public ImageValidator() { RuleFor(x => x.Url).MaximumLength(500).Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")); }
}
public sealed class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Brand).MaximumLength(80);
        RuleFor(x => x.Translations).NotEmpty().Must(t => t.Any(x => x.Lang == "en")).WithMessage("An English translation is required.")
            .Must(t => t.Select(x => x.Lang).Distinct().Count() == t.Count).WithMessage("Duplicate language.");
        RuleForEach(x => x.Translations).SetValidator(new TranslationValidator());
        RuleFor(x => x.Variants).NotEmpty().Must(v => v.Count <= 50).Must(v => v.Select(x => x.Sku.ToUpperInvariant()).Distinct().Count() == v.Count).WithMessage("Duplicate SKU.");
        RuleForEach(x => x.Variants).SetValidator(new VariantValidator());
        RuleFor(x => x.Images).Must(i => i is null || i.Count <= 10);
        RuleForEach(x => x.Images!).SetValidator(new ImageValidator()).When(x => x.Images is not null);
    }
}
public sealed class UpdateProductValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty(); RuleFor(x => x.Brand).MaximumLength(80);
        RuleFor(x => x.Translations).NotEmpty().Must(t => t.Any(x => x.Lang == "en"));
        RuleForEach(x => x.Translations).SetValidator(new TranslationValidator());
    }
}
public sealed class UpdateVariantValidator : AbstractValidator<UpdateVariantRequest>
{ public UpdateVariantValidator() { RuleFor(x => x.PriceCents).GreaterThan(0).LessThan(1_000_000_000); RuleFor(x => x.CompareAtPriceCents).GreaterThan(x => x.PriceCents).When(x => x.CompareAtPriceCents is not null); } }

public static class Slugs
{
    /// <summary>"Nike Air Zoom 2026!" → "nike-air-zoom-2026". char.IsLetterOrDigit is Unicode-aware, so Sinhala/Tamil slugs work in Phase 3.</summary>
    public static string From(string name)
    {
        var sb = new StringBuilder(); var dash = false;
        foreach (var ch in name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormC))    // NFC: one canonical form for Sinhala/Tamil input
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); dash = false; }
            else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
        }
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "product" : s[..Math.Min(s.Length, 80)];
    }

    public static async Task<string> UniqueAsync(CatalogDb db, string lang, string name, Guid? exceptProductId, CancellationToken ct)
    {
        var baseSlug = From(name); var slug = baseSlug;
        while (await db.ProductTranslations.AnyAsync(t => t.Lang == lang && t.Slug == slug && t.ProductId != exceptProductId, ct))
            slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..4]}";
        return slug;
    }
}
```
*(Add `using FluentValidation;` etc. as the IDE suggests. `RuleForEach(x => x.Images!)` on a nullable list is fine with the `.When`.)*

### 1.9.3 Consumer: who is allowed to sell

`Consumers/SellerStatusConsumer.cs`
```csharp
using LankaMart.Catalog.Api.Data;
using LankaMart.Catalog.Api.Domain;

namespace LankaMart.Catalog.Api.Consumers;

public sealed class SellerStatusConsumer(CatalogDb db) :
    IConsumer<SellerApproved>, IConsumer<SellerRejected>, IConsumer<SellerSuspended>, IConsumer<SellerReinstated>
{
    public Task Consume(ConsumeContext<SellerApproved> c)   => Apply(c.Message.SellerId, "Active", c.Message.OccurredAt);
    public Task Consume(ConsumeContext<SellerRejected> c)   => Apply(c.Message.SellerId, "Rejected", c.Message.OccurredAt);
    public Task Consume(ConsumeContext<SellerSuspended> c)  => Apply(c.Message.SellerId, "Suspended", c.Message.OccurredAt);
    public Task Consume(ConsumeContext<SellerReinstated> c) => Apply(c.Message.SellerId, "Active", c.Message.OccurredAt);

    private async Task Apply(Guid sellerId, string status, DateTimeOffset at)
    {
        var s = await db.KnownSellers.FindAsync(sellerId);
        if (s is null) db.KnownSellers.Add(new KnownSeller { SellerId = sellerId, Status = status, UpdatedAt = at });
        else if (at > s.UpdatedAt) { s.Status = status; s.UpdatedAt = at; }      // ignore STALE events: messages can arrive out of order
        await db.SaveChangesAsync();
    }
}
```
🧠 *This is the pattern for "I need data owned by another service": **keep a small read-model copy, updated by events**. Catalog never calls Users, so Users being down doesn't stop product browsing.*

### 1.9.4 Public endpoints (browse, search, detail)

`Features/PublicEndpoints.cs`
```csharp
using LankaMart.Catalog.Api.Data;
using LankaMart.Catalog.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LankaMart.Catalog.Api.Features;

public static class PublicEndpoints
{
    public static void MapPublic(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Catalog (public)").AllowAnonymous();

        g.MapGet("/categories", async (CatalogDb db, string? lang, CancellationToken ct) =>
        {
            var l = Lang.Normalize(lang);
            var cats = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).Select(c => new
            {
                c.Id, c.ParentId, c.Slug,
                Name = c.Translations.Where(t => t.Lang == l || t.Lang == "en").OrderBy(t => t.Lang == l ? 0 : 1).Select(t => t.Name).FirstOrDefault() ?? c.Slug
            }).ToListAsync(ct);
            return Results.Ok(cats);
        });

        g.MapGet("/products", async (CatalogDb db, string? query, Guid? category, long? min, long? max, string? sort, string? lang,
                                     int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct) =>
        {
            var l = Lang.Normalize(lang);
            var (p, size) = Paging.Normalize(page, pageSize);

            // Only ACTIVE products of ACTIVE sellers that have at least one active variant are visible.
            var q = db.Products.AsNoTracking()
                .Where(x => x.Status == ProductStatus.Active && x.Variants.Any(v => v.IsActive)
                         && db.KnownSellers.Any(s => s.SellerId == x.SellerId && s.Status == "Active"));
            if (category is not null) q = q.Where(x => x.CategoryId == category);
            if (!string.IsNullOrWhiteSpace(query))
            {
                var pattern = $"%{Like.Escape(query.Trim()[..Math.Min(query.Trim().Length, 100)])}%";   // cap length + escape wildcards
                q = q.Where(x => x.Translations.Any(t => EF.Functions.ILike(t.Name, pattern, "\\")));
            }
            if (min is not null) q = q.Where(x => x.Variants.Where(v => v.IsActive).Min(v => (long?)v.PriceCents) >= min);
            if (max is not null) q = q.Where(x => x.Variants.Where(v => v.IsActive).Min(v => (long?)v.PriceCents) <= max);

            // sort: explicit ALLOWLIST — never build ORDER BY from raw user input
            q = sort switch
            {
                "price_asc"  => q.OrderBy(x => x.Variants.Where(v => v.IsActive).Min(v => (long?)v.PriceCents)),
                "price_desc" => q.OrderByDescending(x => x.Variants.Where(v => v.IsActive).Min(v => (long?)v.PriceCents)),
                "rating"     => q.OrderByDescending(x => x.RatingAvg).ThenByDescending(x => x.RatingCount),
                _            => q.OrderByDescending(x => x.CreatedAt)
            };

            var total = await q.LongCountAsync(ct);
            var items = await q.Skip((p - 1) * size).Take(size).Select(x => new ProductListItem(
                x.Id,
                x.Translations.Where(t => t.Lang == l || t.Lang == "en").OrderBy(t => t.Lang == l ? 0 : 1).Select(t => t.Slug).FirstOrDefault() ?? "",
                x.Translations.Where(t => t.Lang == l || t.Lang == "en").OrderBy(t => t.Lang == l ? 0 : 1).Select(t => t.Name).FirstOrDefault() ?? "",
                x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                x.Variants.Where(v => v.IsActive).Min(v => (long?)v.PriceCents) ?? 0,
                x.RatingAvg, x.RatingCount, x.SellerId)).ToListAsync(ct);

            return Results.Ok(new PagedResult<ProductListItem>(items, p, size, total));
        });

        g.MapGet("/products/{slug}", async (string slug, string? lang, CatalogDb db, CancellationToken ct) =>
        {
            var l = Lang.Normalize(lang);
            var productId = await db.ProductTranslations.AsNoTracking().Where(t => t.Slug == slug).Select(t => (Guid?)t.ProductId).FirstOrDefaultAsync(ct)
                            ?? throw new NotFoundException("Product not found");
            var x = await db.Products.AsNoTracking().AsSplitQuery()
                .Include(p => p.Translations).Include(p => p.Variants.Where(v => v.IsActive)).Include(p => p.Images)
                .SingleOrDefaultAsync(p => p.Id == productId && p.Status == ProductStatus.Active, ct) ?? throw new NotFoundException("Product not found");
            if (!await db.KnownSellers.AnyAsync(s => s.SellerId == x.SellerId && s.Status == "Active", ct)) throw new NotFoundException("Product not found");

            var tr = x.Translations.FirstOrDefault(t => t.Lang == l) ?? x.Translations.First(t => t.Lang == "en");   // fallback chain: requested → en
            return Results.Ok(new
            {
                x.Id, slug = tr.Slug, name = tr.Name, description = tr.Description, lang = tr.Lang,
                availableLanguages = x.Translations.Select(t => t.Lang), x.Brand, x.CategoryId, x.SellerId, x.RatingAvg, x.RatingCount,
                variants = x.Variants.OrderBy(v => v.PriceCents).Select(v => new
                {
                    v.Id, v.Sku, v.PriceCents, v.CompareAtPriceCents,
                    attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(v.AttributesJson) ?? []
                }),
                images = x.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Url, i.Alt, i.VariantId })
            });
        });
    }
}
```

### 1.9.5 Seller endpoints (the approved-seller gate + ownership)

`Features/SellerEndpoints.cs`
```csharp
using System.Text.Json;
using LankaMart.Catalog.Api.Data;
using LankaMart.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Catalog.Api.Features;

/// <summary>"View-only until approved" enforced at the API (System Scenarios §3.1). A crafty seller calling POST directly with an unapproved token gets 403 SELLER_NOT_APPROVED.</summary>
public sealed class RequireActiveSellerFilter(CatalogDb db) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var sellerId = ctx.HttpContext.User.SellerId();
        if (!await db.KnownSellers.AnyAsync(s => s.SellerId == sellerId && s.Status == "Active"))
            throw new ForbiddenException("SELLER_NOT_APPROVED", "Your seller account is not approved yet.");
        return await next(ctx);
    }
}

public static class SellerEndpoints
{
    public static void MapSeller(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/seller/products").WithTags("Seller products")
                   .RequireAuthorization(Policies.Seller).AddEndpointFilter<RequireActiveSellerFilter>();

        g.MapGet("/", async (HttpContext c, CatalogDb db, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var list = await db.Products.AsNoTracking().Where(p => p.SellerId == sid).OrderByDescending(p => p.CreatedAt)
                .Select(p => new { p.Id, status = p.Status.ToString(), name = p.Translations.Where(t => t.Lang == "en").Select(t => t.Name).FirstOrDefault(), variants = p.Variants.Count, p.CreatedAt }).ToListAsync(ct);
            return Results.Ok(list);
        });

        g.MapPost("/", async (CreateProductRequest r, HttpContext c, CatalogDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            var sid = c.User.SellerId(); var now = clock.GetUtcNow();
            if (!await db.Categories.AnyAsync(x => x.Id == r.CategoryId, ct)) throw new BusinessRuleException("UNKNOWN_CATEGORY", "Category does not exist.");

            var p = new Product { Id = Guid.CreateVersion7(), SellerId = sid, CategoryId = r.CategoryId, Brand = r.Brand?.Trim(), Status = ProductStatus.Draft, CreatedAt = now, UpdatedAt = now };
            foreach (var t in r.Translations)
                p.Translations.Add(new ProductTranslation { ProductId = p.Id, Lang = t.Lang, Name = t.Name.Trim(), Description = t.Description.Trim(), Slug = await Slugs.UniqueAsync(db, t.Lang, t.Name, null, ct) });
            foreach (var v in r.Variants)
                p.Variants.Add(new ProductVariant { Id = Guid.CreateVersion7(), SellerId = sid, Sku = v.Sku, AttributesJson = JsonSerializer.Serialize(v.Attributes ?? []), PriceCents = v.PriceCents, CompareAtPriceCents = v.CompareAtPriceCents });
            var order = 0;
            foreach (var i in r.Images ?? []) p.Images.Add(new ProductImage { Id = Guid.CreateVersion7(), Url = i.Url, Alt = i.Alt, SortOrder = order++ });

            var skus = r.Variants.Select(x => x.Sku).ToList();
            if (await db.Variants.AnyAsync(v => v.SellerId == sid && skus.Contains(v.Sku), ct)) throw new ConflictException("SKU_TAKEN", "One of your SKUs is already used.");
            db.Products.Add(p);
            await db.SaveChangesAsync(ct);
            foreach (var v in p.Variants) await bus.Publish(new VariantCreated(v.Id, p.Id, sid, v.Sku, now), ct);    // Inventory creates the stock row
            return Results.Created($"/api/v1/seller/products/{p.Id}", new { p.Id, status = p.Status.ToString(), variantIds = p.Variants.Select(v => v.Id) });
        }).WithValidation<CreateProductRequest>();

        g.MapPut("/{id:guid}", async (Guid id, UpdateProductRequest r, HttpContext c, CatalogDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var p = await db.Products.Include(x => x.Translations).SingleOrDefaultAsync(x => x.Id == id && x.SellerId == sid, ct) ?? throw new NotFoundException();   // ownership in the query
            if (!await db.Categories.AnyAsync(x => x.Id == r.CategoryId, ct)) throw new BusinessRuleException("UNKNOWN_CATEGORY", "Category does not exist.");
            p.CategoryId = r.CategoryId; p.Brand = r.Brand?.Trim(); p.UpdatedAt = clock.GetUtcNow();
            foreach (var t in r.Translations)
            {
                var row = p.Translations.FirstOrDefault(x => x.Lang == t.Lang);
                if (row is null) p.Translations.Add(new ProductTranslation { ProductId = p.Id, Lang = t.Lang, Name = t.Name.Trim(), Description = t.Description.Trim(), Slug = await Slugs.UniqueAsync(db, t.Lang, t.Name, p.Id, ct) });
                else { row.Name = t.Name.Trim(); row.Description = t.Description.Trim(); }     // slug stays stable so shared links don't break
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithValidation<UpdateProductRequest>();

        g.MapPost("/{id:guid}/variants", async (Guid id, VariantDto v, HttpContext c, CatalogDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            if (!await db.Products.AnyAsync(x => x.Id == id && x.SellerId == sid, ct)) throw new NotFoundException();
            if (await db.Variants.AnyAsync(x => x.SellerId == sid && x.Sku == v.Sku, ct)) throw new ConflictException("SKU_TAKEN", "SKU already used.");
            var nv = new ProductVariant { Id = Guid.CreateVersion7(), ProductId = id, SellerId = sid, Sku = v.Sku, AttributesJson = JsonSerializer.Serialize(v.Attributes ?? []), PriceCents = v.PriceCents, CompareAtPriceCents = v.CompareAtPriceCents };
            db.Variants.Add(nv); await db.SaveChangesAsync(ct);
            await bus.Publish(new VariantCreated(nv.Id, id, sid, nv.Sku, clock.GetUtcNow()), ct);
            return Results.Created($"/api/v1/seller/products/{id}/variants/{nv.Id}", new { nv.Id });
        }).WithValidation<VariantDto>();

        g.MapPut("/{id:guid}/variants/{variantId:guid}", async (Guid id, Guid variantId, UpdateVariantRequest r, HttpContext c, CatalogDb db, CancellationToken ct) =>
        {
            var v = await db.Variants.SingleOrDefaultAsync(x => x.Id == variantId && x.ProductId == id && x.SellerId == c.User.SellerId(), ct) ?? throw new NotFoundException();
            v.PriceCents = r.PriceCents; v.CompareAtPriceCents = r.CompareAtPriceCents; v.IsActive = r.IsActive;
            await db.SaveChangesAsync(ct);   // price edits stay live (System Scenarios §3.4); title/image edits will need re-moderation in Phase 3
            return Results.NoContent();
        }).WithValidation<UpdateVariantRequest>();

        g.MapPost("/{id:guid}/publish", async (Guid id, HttpContext c, CatalogDb db, IPublishEndpoint bus, TimeProvider clock, IConfiguration cfg, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var p = await db.Products.Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == id && x.SellerId == sid, ct) ?? throw new NotFoundException();
            if (p.Status is not (ProductStatus.Draft or ProductStatus.Archived or ProductStatus.Rejected)) throw new BusinessRuleException("INVALID_STATE", $"Cannot publish a {p.Status} product.");
            if (!p.Variants.Any(v => v.IsActive)) throw new BusinessRuleException("NO_ACTIVE_VARIANT", "Add at least one active variant first.");
            var old = p.Status;
            // Phase 3 flips this switch: Draft → PendingModeration → (Seller Manager approves) → Active.
            p.Status = cfg.GetValue("Catalog:ModerationEnabled", false) ? ProductStatus.PendingModeration : ProductStatus.Active;
            p.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await bus.Publish(new ProductStatusChanged(p.Id, sid, old.ToString(), p.Status.ToString(), p.UpdatedAt), ct);
            return Results.Ok(new { status = p.Status.ToString() });
        });

        g.MapPost("/{id:guid}/archive", async (Guid id, HttpContext c, CatalogDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var p = await db.Products.SingleOrDefaultAsync(x => x.Id == id && x.SellerId == sid, ct) ?? throw new NotFoundException();
            var old = p.Status; p.Status = ProductStatus.Archived; p.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await bus.Publish(new ProductStatusChanged(p.Id, sid, old.ToString(), "Archived", p.UpdatedAt), ct);
            return Results.NoContent();
        });
    }
}
```
*(The SKU-duplicate check in `POST /` is deliberately simple; the unique index `(seller_id, sku)` is the real guarantee — a concurrent duplicate surfaces as a DB error you may map to `SKU_TAKEN` as an exercise.)*

### 1.9.6 Internal lookup + seeding + `Program.cs`

`Features/InternalEndpoints.cs`
```csharp
using LankaMart.Catalog.Api.Data;
using LankaMart.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Catalog.Api.Features;

public static class InternalEndpoints
{
    public static void MapInternal(this WebApplication app)
    {
        var g = app.MapInternalGroup("/api/v1/internal");      // JWT not needed, internal key REQUIRED, and the gateway has no route to it

        // Orders asks "what is the REAL price/name/seller of these variants?" — the client's price is never trusted.
        g.MapPost("/variants/lookup", async (VariantLookupRequest req, CatalogDb db, CancellationToken ct) =>
        {
            var ids = req.VariantIds.Distinct().Take(100).ToList();
            var rows = await db.Variants.AsNoTracking().Where(v => ids.Contains(v.Id)).Select(v => new
            {
                v.Id, v.ProductId, v.SellerId, v.Sku, v.PriceCents, v.IsActive, ProductStatus = v.Product.Status,
                Name = v.Product.Translations.Where(t => t.Lang == "en").Select(t => t.Name).FirstOrDefault() ?? v.Sku,
                Image = v.Product.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()
            }).ToListAsync(ct);

            var sellerIds = rows.Select(r => r.SellerId).Distinct().ToList();
            var active = (await db.KnownSellers.Where(s => sellerIds.Contains(s.SellerId) && s.Status == "Active").Select(s => s.SellerId).ToListAsync(ct)).ToHashSet();

            var result = rows.Select(r => new VariantInfo(r.Id, r.ProductId, r.SellerId, r.Sku, r.Name, r.Image, r.PriceCents,
                IsPurchasable: r.IsActive && r.ProductStatus == ProductStatus.Active && active.Contains(r.SellerId))).ToList();
            return Results.Ok(new VariantLookupResponse(result));
        });
    }
}
```

`Data/CatalogSeeder.cs` — reference data (categories) created idempotently:
```csharp
using LankaMart.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace LankaMart.Catalog.Api.Data;

public static class CatalogSeeder
{
    public static async Task SeedAsync(CatalogDb db)
    {
        string[][] cats = [["electronics","Electronics"],["fashion","Fashion"],["home-living","Home & Living"],["beauty-health","Beauty & Health"],
                           ["groceries","Groceries"],["sports-outdoors","Sports & Outdoors"],["books-stationery","Books & Stationery"],["toys-baby","Toys & Baby"]];
        var order = 0;
        foreach (var c in cats)
        {
            if (await db.Categories.AnyAsync(x => x.Slug == c[0])) { order++; continue; }
            var cat = new Category { Id = Guid.CreateVersion7(), Slug = c[0], SortOrder = order++ };
            cat.Translations.Add(new CategoryTranslation { CategoryId = cat.Id, Lang = "en", Name = c[1] });   // si/ta names are added in Phase 3 via the Localization service
            db.Categories.Add(cat);
        }
        await db.SaveChangesAsync();
    }
}
```

`Program.cs`
```csharp
using FluentValidation;
using LankaMart.Catalog.Api.Data;
using LankaMart.Catalog.Api.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("catalog");

builder.Services.AddDbContext<CatalogDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "catalog", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
await app.MigrateDatabaseAsync<CatalogDb>();
using (var scope = app.Services.CreateScope()) await CatalogSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<CatalogDb>());

app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapPublic();
app.MapSeller();
app.MapInternal();
app.Run();

public partial class Program;
```

✅ **Check**
1. `GET :5100/api/v1/categories` → 8 categories (no token needed).
2. As an **unapproved** seller: `POST /api/v1/seller/products` → **403 `SELLER_NOT_APPROVED`**. As an approved seller (§1.8 check 4) → 201. Watch RabbitMQ: `inventory-variant-created` queue receives messages (Inventory isn't built yet; they wait — messages are *durable*, they'll be consumed when it starts. That's the decoupling benefit.)
3. `POST …/{id}/publish` → `Active`. `GET /api/v1/products?query=shoe` finds it; `?query=%` returns **nothing weird** (escaped); `?page_size=100000` returns max 50.
4. **IDOR test:** seller B tries `PUT /seller/products/{A's id}` → **404**.
5. `POST :5100/api/v1/internal/variants/lookup` → 404 (gateway), but straight to `:5103` **with** `X-Internal-Key: dev-internal-key-change-me` → 200; **without** the header → 401.
6. Suspend the seller (as manager) → their products vanish from search within a second (event → `known_sellers`).
7. `EXPLAIN ANALYZE SELECT … WHERE name ILIKE '%shoe%'` in pgAdmin → shows `Bitmap Index Scan on ix_product_translations_name_trgm` once you have a few hundred rows (with few rows Postgres may prefer a sequential scan — that's normal).

⚠️ **Pitfalls:** *"function gin_trgm_ops does not exist"* → `pg_trgm` wasn't created (init script didn't run; reset the volume per §0.6) · *EF can't translate LINQ* → simplify the query or split into two steps; read the exception, it names the offending expression · returning **entities** directly from endpoints leaks internal fields and creates JSON cycles — we always project to DTOs.

Commit: `feat(catalog): products, variants, search, seller gate, internal lookup`.

---

## 1.10 Inventory service

**Port 5104 · schema `inventory` · login `inventory_svc` · Redis.**
**Mission:** *stock truth.* The most **contention-sensitive** service: in a flash sale, 10,000 people try to reserve the same 500 units in a minute.

🧠 **The two-layer design (architecture doc §4.4)**

| Layer | Holds | Why |
|---|---|---|
| **PostgreSQL** (durable truth) | `qty_on_hand`, `qty_reserved`, reservations, stock movements | survives crashes; auditable; the "accounting truth" |
| **Redis** (hot path) | `stock:{variantId}` = *available* (= on-hand − reserved) | one atomic Lua script decides "enough stock?" in microseconds without row locks — the "operational truth" during checkout |

Redis and Postgres can briefly disagree; we accept **eventual consistency with reconciliation** (the reconciler worker arrives in Phase 2) rather than distributed locking.

**Reservation lifecycle**
```
reserve(orderId, lines, ttl)   → Redis atomic decrement ✔ → Postgres reservation row (RESERVED, expires_at)
   ├─ payment succeeds → OrderPaid event → confirm:  on_hand −= n, reserved −= n   (Redis unchanged: already decremented)
   ├─ payment fails / order cancelled / expired → OrderCancelled event → release: reserved −= n, Redis += n
   └─ nobody does anything for ttl → sweeper releases it   ← abandoned carts NEVER lock stock forever
```

**Endpoints**

| Method & path | Who | Purpose |
|---|---|---|
| `GET /api/v1/inventory/availability?variantIds=a,b` | public | `in_stock` / `low_stock` / `out_of_stock` per variant (never exact counts — competitors would scrape them) |
| `GET /api/v1/seller/inventory` | seller | my stock |
| `POST /api/v1/seller/inventory/adjust` | seller | restock / correction **with a reason** (audited in `stock_movements`) |
| `POST /api/v1/internal/inventory/reserve · confirm · release` | Orders (internal key) | the saga's stock steps |

### 1.10.1 Domain & DB

`Domain/Entities.cs`
```csharp
namespace LankaMart.Inventory.Api.Domain;

public class InventoryItem
{
    public Guid VariantId { get; set; }            // PK — one row per variant (single warehouse in Phase 1)
    public Guid SellerId { get; set; }             // learned from VariantCreated → lets us enforce "seller edits only own stock"
    public string Sku { get; set; } = "";
    public int QtyOnHand { get; set; }
    public int QtyReserved { get; set; }
    public int ReorderPoint { get; set; } = 5;
    public int Available => QtyOnHand - QtyReserved;
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum ReservationStatus { Reserved, Confirmed, Released }

public class Reservation
{
    public Guid Id { get; set; }                   // == orderId → makes "reserve" naturally idempotent (same order can't reserve twice)
    public ReservationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public List<ReservationLine> Lines { get; set; } = [];
}
public class ReservationLine { public Guid ReservationId { get; set; } public Guid VariantId { get; set; } public int Quantity { get; set; } }

public class StockMovement                         // append-only audit of every manual change
{
    public Guid Id { get; set; } public Guid VariantId { get; set; } public int Delta { get; set; }
    public string Reason { get; set; } = ""; public Guid ActorId { get; set; } public DateTimeOffset At { get; set; }
}
```

`Data/InventoryDb.cs`
```csharp
using LankaMart.Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace LankaMart.Inventory.Api.Data;

public class InventoryDb(DbContextOptions<InventoryDb> options) : DbContext(options)
{
    public DbSet<InventoryItem> Items => Set<InventoryItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationLine> ReservationLines => Set<ReservationLine>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("inventory");
        b.Entity<InventoryItem>(e =>
        {
            e.HasKey(x => x.VariantId); e.HasIndex(x => x.SellerId); e.Ignore(x => x.Available);
            // DB-level safety net: even a buggy query can never make stock negative or over-reserved
            e.ToTable(t => { t.HasCheckConstraint("ck_stock_nonneg", "qty_on_hand >= 0 AND qty_reserved >= 0 AND qty_reserved <= qty_on_hand"); });
        });
        b.Entity<Reservation>(e => { e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.HasIndex(x => new { x.Status, x.ExpiresAt });
                                     e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ReservationId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<ReservationLine>().HasKey(x => new { x.ReservationId, x.VariantId });
        b.Entity<StockMovement>().HasIndex(x => x.VariantId);
    }
}
```
*(The check-constraint column names assume snake_case; without the naming convention use `"\"QtyOnHand\" >= 0 …"`.)*

### 1.10.2 The Redis stock engine (Lua)

🧠 **Why Lua?** To reserve 3 variants for one order we must check *all* have enough stock and then decrement *all* — as **one indivisible step**. Redis runs a Lua script atomically (no other command can interleave). That is how you survive 10,000 simultaneous buyers: no row locks, no race where two people both "see" the last unit.

`Stock/RedisStock.cs`
```csharp
using LankaMart.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace LankaMart.Inventory.Api.Stock;

public sealed class RedisStock(IConnectionMultiplexer redis, InventoryDb db)
{
    private IDatabase R => redis.GetDatabase();
    public static string Key(Guid variantId) => $"stock:{variantId}";

    // Atomic multi-variant reserve. KEYS = stock keys; ARGV = quantities (same order).
    // returns: 1 = reserved · 0 = insufficient stock · -1 = a counter is missing (cache cold → load from Postgres and retry)
    private const string ReserveLua = """
        local n = #KEYS
        for i = 1, n do
          local s = redis.call('GET', KEYS[i])
          if s == false then return -1 end
          if tonumber(s) < tonumber(ARGV[i]) then return 0 end
        end
        for i = 1, n do redis.call('DECRBY', KEYS[i], ARGV[i]) end
        return 1
        """;

    /// <summary>Make sure counters exist. SET NX = "only if missing", so concurrent loaders can't overwrite a live counter.</summary>
    public async Task EnsureLoadedAsync(IEnumerable<Guid> variantIds, CancellationToken ct)
    {
        foreach (var id in variantIds.Distinct())
        {
            if (await R.KeyExistsAsync(Key(id))) continue;
            var avail = await db.Items.AsNoTracking().Where(i => i.VariantId == id).Select(i => (int?)(i.QtyOnHand - i.QtyReserved)).SingleOrDefaultAsync(ct);
            if (avail is not null) await R.StringSetAsync(Key(id), avail.Value, when: When.NotExists);
        }
    }

    /// <returns>1 ok · 0 insufficient · -1 missing counter</returns>
    public async Task<long> TryReserveAsync(IReadOnlyList<(Guid VariantId, int Qty)> lines)
    {
        var keys = lines.Select(l => (RedisKey)Key(l.VariantId)).ToArray();
        var args = lines.Select(l => (RedisValue)l.Qty).ToArray();
        return (long)await R.ScriptEvaluateAsync(ReserveLua, keys, args);
    }

    public async Task AddBackAsync(IEnumerable<(Guid VariantId, int Qty)> lines)     // release / compensation
    { foreach (var l in lines) await R.StringIncrementAsync(Key(l.VariantId), l.Qty); }

    public async Task AdjustAsync(Guid variantId, int delta)           // restock / correction: keep the Redis counter in step with Postgres
    { if (await R.KeyExistsAsync(Key(variantId))) await R.StringIncrementAsync(Key(variantId), delta); }

    public async Task<int?> GetAsync(Guid variantId) => (int?)await R.StringGetAsync(Key(variantId));
}
```

### 1.10.3 The reservation service (the logic)

`Stock/ReservationService.cs`
```csharp
using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Inventory.Api.Stock;

public sealed class ReservationService(InventoryDb db, RedisStock redis, IPublishEndpoint bus, TimeProvider clock, ILogger<ReservationService> log)
{
    public async Task<ReserveStockResponse> ReserveAsync(ReserveStockRequest req, CancellationToken ct)
    {
        // 1) IDEMPOTENT: Orders retries on timeouts (resilience handler). Same reservation id → same answer, no double-decrement.
        if (await db.Reservations.AnyAsync(r => r.Id == req.ReservationId, ct)) return new(true, []);

        var lines = req.Lines.GroupBy(l => l.VariantId).Select(g => (VariantId: g.Key, Qty: g.Sum(x => x.Quantity))).ToList();
        if (lines.Any(l => l.Qty <= 0 || l.Qty > 100)) throw new BusinessRuleException("INVALID_QUANTITY", "Quantity must be 1–100.");
        var ids = lines.Select(l => l.VariantId).ToList();

        // unknown variants (no inventory row) = unavailable
        var known = await db.Items.AsNoTracking().Where(i => ids.Contains(i.VariantId)).Select(i => i.VariantId).ToListAsync(ct);
        var unknown = ids.Except(known).ToList();
        if (unknown.Count > 0) return new(false, unknown);

        // 2) Redis atomic check-and-decrement (cold counter? load from Postgres once, retry)
        await redis.EnsureLoadedAsync(ids, ct);
        var code = await redis.TryReserveAsync(lines);
        if (code == -1) { await redis.EnsureLoadedAsync(ids, ct); code = await redis.TryReserveAsync(lines); }
        if (code != 1) return new(false, await FindShortfallAsync(lines));

        // 3) Persist. If Postgres refuses (e.g., drift: Redis said yes, DB says no), undo the Redis decrement → never leak stock.
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow();
            foreach (var (variantId, qty) in lines)
            {
                var rows = await db.Items
                    .Where(i => i.VariantId == variantId && i.QtyOnHand - i.QtyReserved >= qty)       // conditional UPDATE = compare-and-set
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.QtyReserved, i => i.QtyReserved + qty).SetProperty(i => i.UpdatedAt, now), ct);
                if (rows == 0) { await tx.RollbackAsync(ct); await redis.AddBackAsync(lines); return new(false, [variantId]); }
            }
            var res = new Reservation { Id = req.ReservationId, Status = ReservationStatus.Reserved, CreatedAt = now, ExpiresAt = now.AddSeconds(Math.Clamp(req.TtlSeconds, 60, 3600)) };
            foreach (var (variantId, qty) in lines) res.Lines.Add(new ReservationLine { ReservationId = res.Id, VariantId = variantId, Quantity = qty });
            db.Reservations.Add(res);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch { await redis.AddBackAsync(lines); throw; }
        // KNOWN GAP: a crash between steps 2 and 3 leaks Redis stock until the Phase-2 reconciler corrects drift. Rare, visible, self-healing.

        await PublishLowStockAsync(ids, ct);
        return new(true, []);
    }

    /// <summary>Called when OrderPaid arrives. Idempotent: only a RESERVED reservation transitions.</summary>
    public async Task ConfirmAsync(Guid reservationId, CancellationToken ct)
    {
        var res = await db.Reservations.Include(r => r.Lines).SingleOrDefaultAsync(r => r.Id == reservationId, ct);
        if (res is null) { await bus.Publish(new ReservationConfirmFailed(reservationId, "RESERVATION_NOT_FOUND", clock.GetUtcNow()), ct); return; }
        if (res.Status == ReservationStatus.Confirmed) return;                                           // duplicate delivery → no-op
        if (res.Status == ReservationStatus.Released)                                                      // swept before payment arrived (race) → try to take stock again
        {
            // The TTL is longer than the payment window so this is rare. If stock is gone, Orders must refund (Phase 5).
            await bus.Publish(new ReservationConfirmFailed(reservationId, "RESERVATION_EXPIRED", clock.GetUtcNow()), ct); return;
        }
        var now = clock.GetUtcNow();
        var flipped = await db.Reservations.Where(r => r.Id == reservationId && r.Status == ReservationStatus.Reserved)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Confirmed), ct);        // atomic claim
        if (flipped == 0) return;
        foreach (var l in res.Lines)
            await db.Items.Where(i => i.VariantId == l.VariantId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.QtyOnHand, i => i.QtyOnHand - l.Quantity).SetProperty(i => i.QtyReserved, i => i.QtyReserved - l.Quantity).SetProperty(i => i.UpdatedAt, now), ct);
        // Redis counter already reflects the sale (decremented at reserve time) → nothing to do there.
    }

    /// <summary>Called on OrderCancelled and by the sweeper. Idempotent via the atomic status flip.</summary>
    public async Task ReleaseAsync(Guid reservationId, CancellationToken ct)
    {
        var res = await db.Reservations.AsNoTracking().Include(r => r.Lines).SingleOrDefaultAsync(r => r.Id == reservationId, ct);
        if (res is null) return;
        var flipped = await db.Reservations.Where(r => r.Id == reservationId && r.Status == ReservationStatus.Reserved)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Released), ct);
        if (flipped == 0) return;                                                                          // already confirmed/released → someone else won the race
        var now = clock.GetUtcNow();
        foreach (var l in res.Lines)
            await db.Items.Where(i => i.VariantId == l.VariantId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.QtyReserved, i => i.QtyReserved - l.Quantity).SetProperty(i => i.UpdatedAt, now), ct);
        await redis.AddBackAsync(res.Lines.Select(l => (l.VariantId, l.Quantity)));
        log.LogInformation("Released reservation {ReservationId}", reservationId);
    }

    private async Task<List<Guid>> FindShortfallAsync(List<(Guid VariantId, int Qty)> lines)
    {
        var bad = new List<Guid>();
        foreach (var l in lines) if ((await redis.GetAsync(l.VariantId) ?? 0) < l.Qty) bad.Add(l.VariantId);
        return bad;
    }

    private async Task PublishLowStockAsync(List<Guid> ids, CancellationToken ct)
    {
        var low = await db.Items.AsNoTracking().Where(i => ids.Contains(i.VariantId) && i.QtyOnHand - i.QtyReserved <= i.ReorderPoint).ToListAsync(ct);
        foreach (var i in low) await bus.Publish(new InventoryLowStock(i.VariantId, i.SellerId, i.Available, i.ReorderPoint, clock.GetUtcNow()), ct);
    }
}
```

🧠 **Read the pattern, it will repeat everywhere:** `UPDATE … WHERE status='RESERVED'` is a **compare-and-set**. Two workers (the payment event and the sweeper) may race; the database lets exactly one flip the status; the loser sees `0 rows` and does nothing. *Concurrency control without locks, and every operation safe to repeat.*

### 1.10.4 Consumers, sweeper, endpoints, `Program.cs`

`Consumers/Consumers.cs`
```csharp
using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Domain;
using LankaMart.Inventory.Api.Stock;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Inventory.Api.Consumers;

/// <summary>A variant was created in Catalog → create its stock row (qty 0, or a demo quantity in Development).</summary>
public sealed class VariantCreatedConsumer(InventoryDb db, IConfiguration cfg) : IConsumer<VariantCreated>
{
    public async Task Consume(ConsumeContext<VariantCreated> ctx)
    {
        var m = ctx.Message;
        if (await db.Items.AnyAsync(i => i.VariantId == m.VariantId)) return;                 // idempotent
        db.Items.Add(new InventoryItem { VariantId = m.VariantId, SellerId = m.SellerId, Sku = m.Sku, QtyOnHand = cfg.GetValue("Inventory:DevDefaultStock", 0), UpdatedAt = m.OccurredAt });
        await db.SaveChangesAsync();
    }
}

public sealed class OrderPaidConsumer(ReservationService svc) : IConsumer<OrderPaid>
{ public Task Consume(ConsumeContext<OrderPaid> ctx) => svc.ConfirmAsync(ctx.Message.OrderId, ctx.CancellationToken); }

public sealed class OrderCancelledConsumer(ReservationService svc) : IConsumer<OrderCancelled>
{ public Task Consume(ConsumeContext<OrderCancelled> ctx) => svc.ReleaseAsync(ctx.Message.OrderId, ctx.CancellationToken); }
```

`Workers/ReservationSweeper.cs`
```csharp
using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Domain;
using LankaMart.Inventory.Api.Stock;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Inventory.Api.Workers;

/// <summary>Safety net #1: reservations nobody confirmed or released are freed after their TTL. This is what stops abandoned carts from locking stock forever.</summary>
public sealed class ReservationSweeper(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ReservationSweeper> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<InventoryDb>();
                var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();
                var now = clock.GetUtcNow();
                var expired = await db.Reservations.AsNoTracking().Where(r => r.Status == ReservationStatus.Reserved && r.ExpiresAt < now).Select(r => r.Id).Take(200).ToListAsync(stop);
                foreach (var id in expired) await svc.ReleaseAsync(id, stop);
                if (expired.Count > 0) log.LogInformation("Sweeper released {Count} expired reservations", expired.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Sweeper iteration failed"); }   // a sweeper must never die
        }
    }
}
```

`Features/Endpoints.cs`
```csharp
using FluentValidation;
using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Domain;
using LankaMart.Inventory.Api.Stock;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Inventory.Api.Features;

public sealed record AdjustStockRequest(Guid VariantId, int Delta, string Reason);
public sealed class AdjustValidator : AbstractValidator<AdjustStockRequest>
{ public AdjustValidator() { RuleFor(x => x.Delta).NotEqual(0).InclusiveBetween(-100000, 100000); RuleFor(x => x.Reason).NotEmpty().MaximumLength(200); } }   // reason mandatory → audit trail

public static class Endpoints
{
    public static void MapInventory(this WebApplication app)
    {
        // PUBLIC: coarse availability only
        app.MapGet("/api/v1/inventory/availability", async (string variantIds, RedisStock redis, InventoryDb db, CancellationToken ct) =>
        {
            var ids = variantIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                                .Where(g => g != Guid.Empty).Distinct().Take(50).ToList();
            await redis.EnsureLoadedAsync(ids, ct);
            var rows = new List<object>();
            foreach (var id in ids)
            {
                var a = await redis.GetAsync(id) ?? 0;
                rows.Add(new { variantId = id, status = a <= 0 ? "out_of_stock" : a <= 5 ? "low_stock" : "in_stock" });
            }
            return Results.Ok(rows);
        }).AllowAnonymous();

        // SELLER
        var s = app.MapGroup("/api/v1/seller/inventory").RequireAuthorization(Policies.Seller);
        s.MapGet("/", async (HttpContext c, InventoryDb db, CancellationToken ct) =>
            Results.Ok(await db.Items.AsNoTracking().Where(i => i.SellerId == c.User.SellerId()).OrderBy(i => i.Sku)
                .Select(i => new { i.VariantId, i.Sku, i.QtyOnHand, i.QtyReserved, available = i.QtyOnHand - i.QtyReserved, i.ReorderPoint }).ToListAsync(ct)));

        s.MapPost("/adjust", async (AdjustStockRequest r, HttpContext c, InventoryDb db, RedisStock redis, TimeProvider clock, CancellationToken ct) =>
        {
            var sid = c.User.SellerId(); var now = clock.GetUtcNow();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Items.Where(i => i.VariantId == r.VariantId && i.SellerId == sid && i.QtyOnHand + r.Delta >= i.QtyReserved)   // ownership AND no-negative-available in one statement
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.QtyOnHand, i => i.QtyOnHand + r.Delta).SetProperty(i => i.UpdatedAt, now), ct);
            if (rows == 0)
                throw await db.Items.AnyAsync(i => i.VariantId == r.VariantId && i.SellerId == sid, ct)
                    ? new BusinessRuleException("WOULD_GO_NEGATIVE", "That adjustment would drop stock below what is already reserved.")
                    : new NotFoundException();                                                         // not yours / doesn't exist → 404 (no existence leak)
            db.Movements.Add(new StockMovement { Id = Guid.CreateVersion7(), VariantId = r.VariantId, Delta = r.Delta, Reason = r.Reason, ActorId = c.User.UserId(), At = now });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            await redis.EnsureLoadedAsync([r.VariantId], ct);                                          // counter must exist before we adjust it
            await redis.AdjustAsync(r.VariantId, r.Delta);
            return Results.NoContent();
        }).WithValidation<AdjustStockRequest>();

        // INTERNAL (Orders → Inventory)
        var i = app.MapInternalGroup("/api/v1/internal/inventory");
        i.MapPost("/reserve", async (ReserveStockRequest r, ReservationService svc, CancellationToken ct) => Results.Ok(await svc.ReserveAsync(r, ct)));
        i.MapPost("/confirm/{reservationId:guid}", async (Guid reservationId, ReservationService svc, CancellationToken ct) => { await svc.ConfirmAsync(reservationId, ct); return Results.NoContent(); });
        i.MapPost("/release/{reservationId:guid}", async (Guid reservationId, ReservationService svc, CancellationToken ct) => { await svc.ReleaseAsync(reservationId, ct); return Results.NoContent(); });
    }
}
```

`Program.cs`
```csharp
using FluentValidation;
using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Features;
using LankaMart.Inventory.Api.Stock;
using LankaMart.Inventory.Api.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("inventory");

builder.Services.AddDbContext<InventoryDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RedisStock>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddHostedService<ReservationSweeper>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformRedis(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "inventory", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
await app.MigrateDatabaseAsync<InventoryDb>();
app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapInventory();
app.Run();

public partial class Program;
```
Dev config extra: `"Inventory": { "DevDefaultStock": 50 }` in `appsettings.Development.json` (every new variant starts with 50 units so you can shop immediately; production default is 0). Migration:
```bash
dotnet ef migrations add Initial --project src/services/inventory/LankaMart.Inventory.Api --output-dir Data/Migrations
```

✅ **Check**
1. Start Inventory → it immediately consumes the `VariantCreated` messages that were waiting in RabbitMQ (§1.9 check 2). `GET /api/v1/seller/inventory` (as the seller) lists the variants with 50 units.
2. `POST /seller/inventory/adjust` `{variantId, delta: 10, reason: "restock"}` → on_hand 60 and a `stock_movements` row; delta `-1000` → 422 `WOULD_GO_NEGATIVE`; another seller's variant → 404.
3. Call reserve straight on `:5104` with the internal key:
```bash
curl -X POST http://localhost:5104/api/v1/internal/inventory/reserve -H "X-Internal-Key: dev-internal-key-change-me" -H "Content-Type: application/json" \
  -d '{"reservationId":"11111111-1111-1111-1111-111111111111","lines":[{"variantId":"<id>","quantity":2}],"ttlSeconds":120}'
```
→ `{"success":true}`; repeat the **same** call → still success, stock decremented **once** (idempotency). Check `redis-cli GET stock:<id>` = 58 and `qty_reserved`=2 in Postgres. Ask for 9999 → `success:false` with the variant in `unavailableVariantIds`.
4. Wait > 2 minutes (TTL 120 s) → the sweeper log says it released; Redis back to 60, `qty_reserved` 0, status `Released`.
5. **Race test (the fun one):** run `python` or `k6`/`hey` firing 200 parallel reserve calls (different reservation ids, qty 1) at a variant with 50 units → **exactly 50 succeed**, 150 fail, stock never negative. Write this as an automated integration test (§1.16).

⚠️ **Pitfalls:** forgetting that **unit conversions of `RedisValue`** to `int` can throw if the key holds a non-number · calling Redis `GET` then `SET` yourself (two commands) instead of Lua → race conditions; the whole point is atomicity · releasing by "adding back" without a status guard → double release inflates stock (that's why every release goes through the atomic status flip).

Commit: `feat(inventory): redis lua reservations, sweeper, seller stock`.

---

## 1.11 Order service (the orchestrator)

**Port 5105 · schema `orders` · login `orders_svc` · Redis.**
**Mission:** own the **cart**, run the **checkout saga**, own the **order lifecycle** and **tracking timeline**. The most important service in the system — go slowly here.

**Endpoints**

| Method & path | Who | Purpose |
|---|---|---|
| `GET /api/v1/cart` · `POST /cart/items` · `PATCH /cart/items/{variantId}` · `DELETE /cart/items/{variantId}` · `POST /cart/merge` | guest **or** user | cart (guest identified by `X-Guest-Id`) |
| `POST /api/v1/orders` (**`Idempotency-Key` header**) | verified customer | checkout → starts the saga |
| `GET /api/v1/orders` · `GET /api/v1/orders/{id}` | owner / admin | list / detail |
| `GET /api/v1/orders/{id}/tracking` | owner / admin | the status timeline |
| `POST /api/v1/orders/{id}/cancel` | owner | cancel **while pending payment** (Phase 1) |
| `POST /api/v1/dev/orders/{id}/advance` | super_admin, dev only | simulate logistics until Phase 4 |

### 1.11.1 Domain model

`Domain/Order.cs`
```csharp
namespace LankaMart.Orders.Api.Domain;

public enum OrderStatus
{
    PendingPayment, Paid, Processing, ReadyForPickup, PickedUp, AtHub, OutForDelivery, Delivered, Completed,   // happy path (architecture doc §4.5)
    Cancelled, PaymentFailed, Expired, RefundPending, Refunded                                                 // exits
}

public class Order
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = "";                 // human friendly: LM-2026-880123
    public Guid CustomerId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public long SubtotalCents { get; set; } public long DeliveryFeeCents { get; set; } public long DiscountCents { get; set; } public long TotalCents { get; set; }
    public string Currency { get; set; } = "LKR";
    public Guid? PaymentId { get; set; }
    public AddressSnapshot DeliveryAddress { get; set; } = new();  // SNAPSHOT: editing the address book later never changes where this order ships
    public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset PaymentExpiresAt { get; set; }
    public string? CancelReason { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public List<Shipment> Shipments { get; set; } = [];
    public List<OrderEvent> Events { get; set; } = [];
}

public class AddressSnapshot
{
    public string RecipientName { get; set; } = ""; public string Phone { get; set; } = "";
    public string Line1 { get; set; } = ""; public string? Line2 { get; set; }
    public string City { get; set; } = ""; public string District { get; set; } = ""; public string? PostalCode { get; set; }
}

public class OrderItem
{
    public Guid Id { get; set; } public Guid OrderId { get; set; }
    public Guid VariantId { get; set; } public Guid ProductId { get; set; } public Guid SellerId { get; set; }   // plain ids: NO foreign key to another service's tables
    public string ProductNameSnapshot { get; set; } = ""; public string Sku { get; set; } = ""; public string? ImageUrlSnapshot { get; set; }
    public int Quantity { get; set; } public long UnitPriceCents { get; set; }                                   // frozen at purchase time
    public long LineTotalCents => UnitPriceCents * Quantity;
}

/// <summary>One order can span several sellers → one shipment per seller; each will later travel independently (pickup → hub → delivery).</summary>
public class Shipment { public Guid Id { get; set; } public Guid OrderId { get; set; } public Guid SellerId { get; set; } public OrderStatus Status { get; set; } = OrderStatus.PendingPayment; }

public class OrderEvent   // append-only: the tracking UI is literally a rendering of this table
{
    public Guid Id { get; set; } public Guid OrderId { get; set; }
    public OrderStatus? FromStatus { get; set; } public OrderStatus ToStatus { get; set; }
    public string Actor { get; set; } = "system"; public string? Note { get; set; } public DateTimeOffset At { get; set; }
}

/// <summary>PURE logic (no database) → trivially unit-testable. Every transition in the system goes through here.</summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.PendingPayment] = [OrderStatus.Paid, OrderStatus.Cancelled, OrderStatus.PaymentFailed, OrderStatus.Expired],
        [OrderStatus.Paid]           = [OrderStatus.Processing, OrderStatus.RefundPending, OrderStatus.Cancelled],
        [OrderStatus.Processing]     = [OrderStatus.ReadyForPickup, OrderStatus.RefundPending, OrderStatus.Cancelled],
        [OrderStatus.ReadyForPickup] = [OrderStatus.PickedUp, OrderStatus.RefundPending],
        [OrderStatus.PickedUp]       = [OrderStatus.AtHub],
        [OrderStatus.AtHub]          = [OrderStatus.OutForDelivery],
        [OrderStatus.OutForDelivery] = [OrderStatus.Delivered, OrderStatus.AtHub],          // AtHub again = failed delivery attempt
        [OrderStatus.Delivered]      = [OrderStatus.Completed, OrderStatus.RefundPending],
        [OrderStatus.RefundPending]  = [OrderStatus.Refunded],
    };
    public static bool CanTransition(OrderStatus from, OrderStatus to) => Allowed.TryGetValue(from, out var t) && t.Contains(to);
    public static void EnsureAllowed(OrderStatus from, OrderStatus to)
    { if (!CanTransition(from, to)) throw new BusinessRuleException("INVALID_TRANSITION", $"Order cannot move from {from} to {to}."); }

    /// <summary>The next step on the happy path (used by the dev simulator).</summary>
    public static OrderStatus? Next(OrderStatus s) => s switch
    {
        OrderStatus.Paid => OrderStatus.Processing, OrderStatus.Processing => OrderStatus.ReadyForPickup, OrderStatus.ReadyForPickup => OrderStatus.PickedUp,
        OrderStatus.PickedUp => OrderStatus.AtHub, OrderStatus.AtHub => OrderStatus.OutForDelivery, OrderStatus.OutForDelivery => OrderStatus.Delivered,
        OrderStatus.Delivered => OrderStatus.Completed, _ => null
    };
}
```

`Data/OrdersDb.cs`
```csharp
using LankaMart.Orders.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace LankaMart.Orders.Api.Data;

public class OrdersDb(DbContextOptions<OrdersDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> Items => Set<OrderItem>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<OrderEvent> Events => Set<OrderEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("orders");
        b.HasSequence<long>("order_number_seq").StartsAt(880001);          // LM-2026-880001, 880002, … never repeats, safe under concurrency
        b.Entity<Order>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.OrderNumber).IsUnique(); e.HasIndex(x => new { x.CustomerId, x.CreatedAt }); e.HasIndex(x => new { x.Status, x.PaymentExpiresAt });
            e.OwnsOne(x => x.DeliveryAddress);                                         // columns delivery_address_line1 etc. in the same table
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Shipments).WithOne().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(ev => ev.OrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<OrderItem>().Ignore(x => x.LineTotalCents);
        b.Entity<Shipment>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Entity<OrderEvent>(e => { e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30); e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30); e.HasIndex(x => x.OrderId); });
    }
}
```

### 1.11.2 Packages, config, typed clients to other services

Packages as Users, **plus nothing extra** (Redis/HTTP resilience come from BuildingBlocks). Config = template §1.7.0 with `Username=orders_svc`, and:
```json
"Orders": { "PaymentWindowMinutes": 15, "ReservationTtlMinutes": 17, "DeliveryFeePerShipmentCents": 35000, "MaxQtyPerItem": 10 }
```
🧠 *Why is the reservation TTL (17) longer than the payment window (15)?* So a payment that completes in the last seconds still finds its stock reserved. The order expires at 15; the stock is freed at 17. A 2-minute buffer removes a nasty race.

`Clients/Clients.cs`
```csharp
using System.Net.Http.Json;
namespace LankaMart.Orders.Api.Clients;

// Interfaces make CheckoutService testable: tests swap in fakes, no HTTP needed.
public interface ICatalogClient   { Task<IReadOnlyList<VariantInfo>> LookupAsync(IEnumerable<Guid> variantIds, CancellationToken ct); }
public interface IInventoryClient { Task<ReserveStockResponse> ReserveAsync(ReserveStockRequest req, CancellationToken ct); }
public interface IPaymentsClient  { Task<CreatePaymentIntentResponse> CreateIntentAsync(CreatePaymentIntentRequest req, CancellationToken ct); }

public sealed class CatalogClient(HttpClient http) : ICatalogClient
{
    public async Task<IReadOnlyList<VariantInfo>> LookupAsync(IEnumerable<Guid> variantIds, CancellationToken ct)
    {
        var res = await http.PostAsJsonAsync("/api/v1/internal/variants/lookup", new VariantLookupRequest(variantIds.Distinct().ToList()), ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<VariantLookupResponse>(ct))!.Variants;
    }
}
public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public async Task<ReserveStockResponse> ReserveAsync(ReserveStockRequest req, CancellationToken ct)
    {
        var res = await http.PostAsJsonAsync("/api/v1/internal/inventory/reserve", req, ct);   // safe to retry: reservation id makes it idempotent
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ReserveStockResponse>(ct))!;
    }
}
public sealed class PaymentsClient(HttpClient http) : IPaymentsClient
{
    public async Task<CreatePaymentIntentResponse> CreateIntentAsync(CreatePaymentIntentRequest req, CancellationToken ct)
    {
        var res = await http.PostAsJsonAsync("/api/v1/internal/payments/intent", req, ct);     // safe to retry: Payments is idempotent on orderId
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<CreatePaymentIntentResponse>(ct))!;
    }
}
```

### 1.11.3 The cart (Redis)

🧠 **Why Redis for the cart?** It is temporary, changes constantly, and must be fast. A hash per cart: `cart:u:{userId}` / `cart:g:{guestId}` → field = variantId, value = quantity. **We store only ids and quantities**, never prices — the price is looked up fresh from Catalog whenever the cart is shown or an order is placed (a cart saved last week must not keep last week's price).

`Features/CartService.cs`
```csharp
using LankaMart.Orders.Api.Clients;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace LankaMart.Orders.Api.Features;

public sealed class OrdersOptions { public int PaymentWindowMinutes { get; set; } = 15; public int ReservationTtlMinutes { get; set; } = 17; public long DeliveryFeePerShipmentCents { get; set; } = 35000; public int MaxQtyPerItem { get; set; } = 10; }

public sealed record CartLineDto(Guid VariantId, int Quantity, string? Name, string? ImageUrl, long UnitPriceCents, long LineTotalCents, bool Available);
public sealed record CartDto(IReadOnlyList<CartLineDto> Lines, long SubtotalCents);

public sealed class CartService(IConnectionMultiplexer redis, ICatalogClient catalog, IOptions<OrdersOptions> opt)
{
    private IDatabase Db => redis.GetDatabase();
    public static string UserKey(Guid id) => $"cart:u:{id}";
    public static string GuestKey(Guid id) => $"cart:g:{id}";

    /// <summary>Who owns this cart? Logged-in → by token. Guest → by X-Guest-Id header (a random GUID the client invents and keeps in localStorage).</summary>
    public static string KeyFor(HttpContext c)
    {
        if (c.User.TryUserId() is { } uid) return UserKey(uid);
        if (Guid.TryParse(c.Request.Headers["X-Guest-Id"].FirstOrDefault(), out var gid)) return GuestKey(gid);
        throw new BusinessRuleException("GUEST_ID_REQUIRED", "Send an X-Guest-Id header (a GUID) or sign in.");
    }

    public async Task SetAsync(string key, Guid variantId, int qty, bool add)
    {
        if (qty < 0 || qty > opt.Value.MaxQtyPerItem) throw new BusinessRuleException("INVALID_QUANTITY", $"Quantity must be 0–{opt.Value.MaxQtyPerItem}.");
        if (qty == 0) { await Db.HashDeleteAsync(key, variantId.ToString()); return; }
        if (await Db.HashLengthAsync(key) >= 50 && !await Db.HashExistsAsync(key, variantId.ToString())) throw new BusinessRuleException("CART_FULL", "Cart can hold up to 50 different items.");
        var newQty = add ? (int)await Db.HashIncrementAsync(key, variantId.ToString(), qty) : qty;
        if (add && newQty > opt.Value.MaxQtyPerItem) { await Db.HashSetAsync(key, variantId.ToString(), opt.Value.MaxQtyPerItem); }
        else if (!add) await Db.HashSetAsync(key, variantId.ToString(), qty);
        await Db.KeyExpireAsync(key, key.StartsWith("cart:g:") ? TimeSpan.FromDays(30) : TimeSpan.FromDays(90));
    }

    public async Task<Dictionary<Guid, int>> RawAsync(string key) =>
        (await Db.HashGetAllAsync(key)).ToDictionary(e => Guid.Parse(e.Name!), e => (int)e.Value);

    public async Task ClearAsync(string key) => await Db.KeyDeleteAsync(key);

    /// <summary>After login: fold the guest cart into the user's cart (max quantity wins), then delete the guest cart.</summary>
    public async Task MergeAsync(Guid guestId, Guid userId)
    {
        var guest = await RawAsync(GuestKey(guestId));
        foreach (var (variantId, qty) in guest)
        {
            var current = (int?)await Db.HashGetAsync(UserKey(userId), variantId.ToString()) ?? 0;
            await Db.HashSetAsync(UserKey(userId), variantId.ToString(), Math.Min(Math.Max(current, qty), opt.Value.MaxQtyPerItem));
        }
        await Db.KeyDeleteAsync(GuestKey(guestId));
    }

    public async Task<CartDto> ViewAsync(string key, CancellationToken ct)
    {
        var raw = await RawAsync(key);
        if (raw.Count == 0) return new([], 0);
        var info = (await catalog.LookupAsync(raw.Keys, ct)).ToDictionary(v => v.VariantId);
        var lines = raw.Select(kv =>
        {
            info.TryGetValue(kv.Key, out var v);
            var price = v?.PriceCents ?? 0;
            return new CartLineDto(kv.Key, kv.Value, v?.ProductName, v?.ImageUrl, price, price * kv.Value, v?.IsPurchasable ?? false);
        }).ToList();
        return new(lines, lines.Where(l => l.Available).Sum(l => l.LineTotalCents));
    }

    public async Task RestoreAsync(Guid userId, IEnumerable<(Guid VariantId, int Qty)> lines)   // used when a payment fails: put the items back
    { foreach (var (v, q) in lines) await Db.HashSetAsync(UserKey(userId), v.ToString(), q); }
}
```

### 1.11.4 Checkout — the saga

🧠 **Read this code slowly; it is the whole course in one class.** The order row *is* the saga state. Each step persists before the next external call, and every failure path ends in a consistent state.

`Features/CheckoutService.cs`
```csharp
using FluentValidation;
using LankaMart.Orders.Api.Clients;
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace LankaMart.Orders.Api.Features;

public sealed record PlaceOrderRequest(AddressInput DeliveryAddress);
public sealed record AddressInput(string RecipientName, string Phone, string Line1, string? Line2, string City, string District, string? PostalCode);
public sealed record CheckoutResult(Guid OrderId, string OrderNumber, long TotalCents, string Currency, Guid PaymentId, DateTimeOffset PaymentExpiresAt);

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.DeliveryAddress).NotNull().ChildRules(a =>
        {
            a.RuleFor(x => x.RecipientName).Length(2, 100);
            a.RuleFor(x => x.Phone).Must(p => PhoneNumber.TryNormalizeLk(p, out _));
            a.RuleFor(x => x.Line1).NotEmpty().MaximumLength(150); a.RuleFor(x => x.Line2).MaximumLength(150);
            a.RuleFor(x => x.City).NotEmpty().MaximumLength(80);
            a.RuleFor(x => x.District).Must(d => SriLanka.Districts.Contains(d));
            a.RuleFor(x => x.PostalCode).Matches(@"^\d{5}$").When(x => !string.IsNullOrEmpty(x.PostalCode));
        });
    }
}

public sealed class CheckoutService(OrdersDb db, CartService carts, ICatalogClient catalog, IInventoryClient inventory, IPaymentsClient payments,
    IPublishEndpoint bus, IConnectionMultiplexer redis, IOptions<OrdersOptions> opt, TimeProvider clock, ILogger<CheckoutService> log)
{
    // ───────────── Step 0: idempotency wrapper ─────────────
    public async Task<CheckoutResult> PlaceOrderAsync(Guid userId, PlaceOrderRequest req, string idemKey, CancellationToken ct)
    {
        var key = $"idem:orders:{userId}:{idemKey}";
        var r = redis.GetDatabase();
        // SET NX: only the FIRST request with this key wins the right to create the order.
        if (!await r.StringSetAsync(key, "pending", TimeSpan.FromHours(24), When.NotExists))
        {
            string? v = await r.StringGetAsync(key);
            if (v is null or "pending") throw new ConflictException("REQUEST_IN_PROGRESS", "This checkout is already being processed.");
            return await LoadResultAsync(Guid.Parse(v), userId, ct);                  // retry of a finished request → same answer, no duplicate order
        }
        try
        {
            var result = await PlaceCoreAsync(userId, req, ct);
            await r.StringSetAsync(key, result.OrderId.ToString(), TimeSpan.FromHours(24));
            return result;
        }
        catch { await r.KeyDeleteAsync(key); throw; }                                // failed attempt → allow a fresh retry with the same key
    }

    private async Task<CheckoutResult> PlaceCoreAsync(Guid userId, PlaceOrderRequest req, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // ───────────── Step 1: read the cart ─────────────
        var cartKey = CartService.UserKey(userId);
        var cart = await carts.RawAsync(cartKey);
        if (cart.Count == 0) throw new BusinessRuleException("CART_EMPTY", "Your cart is empty.");

        // ───────────── Step 2: AUTHORITATIVE prices from Catalog (the client's numbers are never trusted) ─────────────
        var info = (await catalog.LookupAsync(cart.Keys, ct)).ToDictionary(v => v.VariantId);
        var bad = cart.Keys.Where(id => !info.TryGetValue(id, out var v) || !v.IsPurchasable).ToList();
        if (bad.Count > 0) throw new ConflictException("ITEM_UNAVAILABLE", "Some items are no longer available. Please review your cart.");

        // ───────────── Step 3: create the order (state PENDING_PAYMENT) — saga state is now durable ─────────────
        var orderNumber = $"LM-{now.Year}-{await NextSequenceAsync(ct)}";
        PhoneNumber.TryNormalizeLk(req.DeliveryAddress.Phone, out var phone);
        var a = req.DeliveryAddress;
        var order = new Order
        {
            Id = Guid.CreateVersion7(), OrderNumber = orderNumber, CustomerId = userId, Status = OrderStatus.PendingPayment, Currency = "LKR",
            CreatedAt = now, UpdatedAt = now, PaymentExpiresAt = now.AddMinutes(opt.Value.PaymentWindowMinutes),
            DeliveryAddress = new AddressSnapshot { RecipientName = a.RecipientName.Trim(), Phone = phone, Line1 = a.Line1.Trim(), Line2 = a.Line2?.Trim(), City = a.City.Trim(), District = a.District, PostalCode = a.PostalCode }
        };
        foreach (var (variantId, qty) in cart)
        {
            var v = info[variantId];
            order.Items.Add(new OrderItem { Id = Guid.CreateVersion7(), OrderId = order.Id, VariantId = variantId, ProductId = v.ProductId, SellerId = v.SellerId,
                                            ProductNameSnapshot = v.ProductName, Sku = v.Sku, ImageUrlSnapshot = v.ImageUrl, Quantity = qty, UnitPriceCents = v.PriceCents });
        }
        foreach (var sellerId in order.Items.Select(i => i.SellerId).Distinct())
            order.Shipments.Add(new Shipment { Id = Guid.CreateVersion7(), OrderId = order.Id, SellerId = sellerId });
        order.SubtotalCents = order.Items.Sum(i => i.LineTotalCents);
        order.DeliveryFeeCents = order.Shipments.Count * opt.Value.DeliveryFeePerShipmentCents;       // one flat fee per seller shipment in Phase 1
        order.TotalCents = order.SubtotalCents + order.DeliveryFeeCents - order.DiscountCents;
        order.Events.Add(new OrderEvent { Id = Guid.CreateVersion7(), OrderId = order.Id, ToStatus = OrderStatus.PendingPayment, Actor = "customer", At = now, Note = "Order placed" });
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        // ───────────── Step 4: reserve stock (sync, atomic in Redis) ─────────────
        ReserveStockResponse reserve;
        try
        {
            reserve = await inventory.ReserveAsync(new ReserveStockRequest(order.Id, order.Items.Select(i => new StockLine(i.VariantId, i.Quantity)).ToList(),
                                                    (int)TimeSpan.FromMinutes(opt.Value.ReservationTtlMinutes).TotalSeconds), ct);
        }
        catch (Exception ex) { await CompensateAsync(order, OrderStatus.Cancelled, "INVENTORY_UNAVAILABLE", ex); throw new ServiceUnavailableException("INVENTORY_UNAVAILABLE", "We couldn't check stock. Please try again."); }

        if (!reserve.Success)
        {
            await CompensateAsync(order, OrderStatus.Cancelled, "OUT_OF_STOCK", null);
            throw new ConflictException("OUT_OF_STOCK", "Sorry, some items just sold out. Please review your cart.");
        }

        // ───────────── Step 5: create the payment intent ─────────────
        CreatePaymentIntentResponse intent;
        try { intent = await payments.CreateIntentAsync(new CreatePaymentIntentRequest(order.Id, userId, order.TotalCents, order.Currency), ct); }
        catch (Exception ex)
        {
            await CompensateAsync(order, OrderStatus.Cancelled, "PAYMENT_UNAVAILABLE", ex);          // COMPENSATION: undo the reservation (via OrderCancelled → Inventory releases)
            throw new ServiceUnavailableException("PAYMENT_UNAVAILABLE", "Payments are temporarily unavailable. Please try again.");
        }
        order.PaymentId = intent.PaymentId;
        await db.SaveChangesAsync(ct);

        // ───────────── Step 6: success path — clear the cart, announce, answer ─────────────
        await carts.ClearAsync(cartKey);
        await bus.Publish(new OrderCreated(order.Id, order.OrderNumber, userId, order.TotalCents, now), ct);
        return new CheckoutResult(order.Id, order.OrderNumber, order.TotalCents, order.Currency, intent.PaymentId, order.PaymentExpiresAt);
    }

    /// <summary>Compensating action: move the order to a terminal state and publish OrderCancelled so Inventory releases the stock.
    /// If the publish is lost, the 17-minute reservation TTL + sweeper is the backstop.</summary>
    private async Task CompensateAsync(Order order, OrderStatus to, string reason, Exception? ex)
    {
        if (ex is not null) log.LogError(ex, "Checkout compensation for {OrderId}: {Reason}", order.Id, reason);
        var changed = await OrderTransitions.TryAsync(db, order.Id, OrderStatus.PendingPayment, to, "system", reason, clock.GetUtcNow(), reason);
        if (changed) await bus.Publish(new OrderCancelled(order.Id, order.CustomerId, reason, ToLines(order), clock.GetUtcNow()));
    }

    public static IReadOnlyList<OrderLine> ToLines(Order o) => o.Items.Select(i => new OrderLine(i.VariantId, i.SellerId, i.Quantity, i.UnitPriceCents)).ToList();

    private async Task<string> NextSequenceAsync(CancellationToken ct) =>
        (await db.Database.SqlQuery<long>($"SELECT nextval('orders.order_number_seq') AS \"Value\"").SingleAsync(ct)).ToString();

    private async Task<CheckoutResult> LoadResultAsync(Guid orderId, Guid userId, CancellationToken ct)
    {
        var o = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId && x.CustomerId == userId, ct) ?? throw new NotFoundException();
        return new CheckoutResult(o.Id, o.OrderNumber, o.TotalCents, o.Currency, o.PaymentId ?? Guid.Empty, o.PaymentExpiresAt);
    }
}
```

`Features/OrderTransitions.cs` — the **compare-and-set** every status change uses:
```csharp
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Orders.Api.Features;

public static class OrderTransitions
{
    /// <summary>Atomically move an order from <paramref name="from"/> to <paramref name="to"/> ONLY IF it is currently in <paramref name="from"/>.
    /// Returns false if someone else already changed it (e.g., payment succeeded while the expiry sweeper was running) — the caller then does nothing.
    /// This single pattern removes whole classes of race conditions without locks.</summary>
    public static async Task<bool> TryAsync(OrdersDb db, Guid orderId, OrderStatus from, OrderStatus to, string actor, string? note, DateTimeOffset now, string? cancelReason = null)
    {
        OrderStateMachine.EnsureAllowed(from, to);
        var rows = await db.Orders.Where(o => o.Id == orderId && o.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, to).SetProperty(o => o.UpdatedAt, now).SetProperty(o => o.CancelReason, o => cancelReason ?? o.CancelReason));
        if (rows == 0) return false;
        await db.Shipments.Where(s => s.OrderId == orderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, to));   // Phase 1: shipments mirror the order; Phase 4 makes them independent
        db.Events.Add(new OrderEvent { Id = Guid.CreateVersion7(), OrderId = orderId, FromStatus = from, ToStatus = to, Actor = actor, Note = note, At = now });
        await db.SaveChangesAsync();
        return true;
    }
}
```

### 1.11.5 Consumers (payment results) and the expiry sweeper

`Consumers/PaymentConsumers.cs`
```csharp
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using LankaMart.Orders.Api.Features;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Orders.Api.Consumers;

public sealed class PaymentCompletedConsumer(OrdersDb db, IPublishEndpoint bus, TimeProvider clock) : IConsumer<PaymentCompleted>
{
    public async Task Consume(ConsumeContext<PaymentCompleted> ctx)
    {
        var m = ctx.Message; var now = clock.GetUtcNow();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == m.OrderId);
        if (order is null) return;                                                          // unknown order → nothing to do (logged by MassTransit)
        if (order.Status != OrderStatus.PendingPayment)
        {
            if (order.Status is not (OrderStatus.Cancelled or OrderStatus.Expired or OrderStatus.PaymentFailed)) return;   // already Paid or further along → duplicate delivery → idempotent no-op
            // Paid AFTER we expired/cancelled the order: the customer's money is taken but there is no order. Tell Payments → refund flow (Phase 5).
            await bus.Publish(new OrderPaymentOrphaned(order.Id, m.PaymentId, now)); return;
        }
        if (!await OrderTransitions.TryAsync(db, order.Id, OrderStatus.PendingPayment, OrderStatus.Paid, "payment", $"Payment {m.GatewayRef}", now)) return;   // lost a race → someone else handled it
        await bus.Publish(new OrderPaid(order.Id, order.OrderNumber, order.CustomerId, order.TotalCents, CheckoutService.ToLines(order), now));            // Inventory confirms stock
        await bus.Publish(new OrderStatusChanged(order.Id, order.CustomerId, "PendingPayment", "Paid", now));
    }
}

public sealed class PaymentFailedConsumer(OrdersDb db, IPublishEndpoint bus, CartService carts, TimeProvider clock) : IConsumer<PaymentFailed>
{
    public async Task Consume(ConsumeContext<PaymentFailed> ctx)
    {
        var m = ctx.Message; var now = clock.GetUtcNow();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == m.OrderId);
        if (order is null) return;
        // COMPENSATION: PENDING_PAYMENT → PAYMENT_FAILED, release stock, give the customer their cart back so they can retry
        if (!await OrderTransitions.TryAsync(db, order.Id, OrderStatus.PendingPayment, OrderStatus.PaymentFailed, "payment", m.FailureCode, now, m.FailureCode)) return;
        await bus.Publish(new OrderCancelled(order.Id, order.CustomerId, "PAYMENT_FAILED:" + m.FailureCode, CheckoutService.ToLines(order), now));
        await carts.RestoreAsync(order.CustomerId, order.Items.Select(i => (i.VariantId, i.Quantity)));
        await bus.Publish(new OrderStatusChanged(order.Id, order.CustomerId, "PendingPayment", "PaymentFailed", now));
    }
}

public sealed class ReservationConfirmFailedConsumer(OrdersDb db, TimeProvider clock, ILogger<ReservationConfirmFailedConsumer> log) : IConsumer<ReservationConfirmFailed>
{
    public async Task Consume(ConsumeContext<ReservationConfirmFailed> ctx)
    {   // Paid but stock could not be confirmed (oversold). System Scenarios §2.5: "auto-refund + apology". The refund itself is built in Phase 5.
        log.LogError("OVERSOLD order {OrderId}: {Reason} — needs refund", ctx.Message.OrderId, ctx.Message.Reason);
        await OrderTransitions.TryAsync(db, ctx.Message.OrderId, OrderStatus.Paid, OrderStatus.RefundPending, "system", ctx.Message.Reason, clock.GetUtcNow());
    }
}
```

`Workers/ExpireStaleOrdersWorker.cs`
```csharp
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using LankaMart.Orders.Api.Features;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Orders.Api.Workers;

/// <summary>Saga timeout: an order still PENDING_PAYMENT after its window is EXPIRED and its stock released (System Scenarios §2.5).</summary>
public sealed class ExpireStaleOrdersWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ExpireStaleOrdersWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrdersDb>(); var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
                var now = clock.GetUtcNow();
                var stale = await db.Orders.AsNoTracking().Include(o => o.Items).Where(o => o.Status == OrderStatus.PendingPayment && o.PaymentExpiresAt < now).Take(100).ToListAsync(stop);
                foreach (var o in stale)
                {
                    if (!await OrderTransitions.TryAsync(db, o.Id, OrderStatus.PendingPayment, OrderStatus.Expired, "system", "Payment window elapsed", now, "PAYMENT_TIMEOUT")) continue;
                    await bus.Publish(new OrderCancelled(o.Id, o.CustomerId, "PAYMENT_TIMEOUT", CheckoutService.ToLines(o), now), stop);
                    log.LogInformation("Order {OrderNumber} expired", o.OrderNumber);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Expiry sweep failed"); }
        }
    }
}
```

### 1.11.6 Endpoints & `Program.cs`

`Features/Endpoints.cs`
```csharp
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Orders.Api.Features;

public sealed record CartItemRequest(Guid VariantId, int Quantity);
public sealed record QuantityRequest(int Quantity);
public sealed record MergeRequest(Guid GuestId);

public static class Endpoints
{
    private static bool IsAdmin(System.Security.Claims.ClaimsPrincipal u) => u.IsInRole(Roles.SuperAdmin) || u.IsInRole(Roles.CustomerManager) || u.IsInRole(Roles.AuditManager);

    public static void MapOrders(this WebApplication app)
    {
        // ───── CART: anonymous allowed (guests). Identity = token OR X-Guest-Id ─────
        var cart = app.MapGroup("/api/v1/cart").WithTags("Cart").AllowAnonymous();
        cart.MapGet("/", async (HttpContext c, CartService s, CancellationToken ct) => Results.Ok(await s.ViewAsync(CartService.KeyFor(c), ct)));
        cart.MapPost("/items", async (CartItemRequest r, HttpContext c, CartService s) => { await s.SetAsync(CartService.KeyFor(c), r.VariantId, r.Quantity, add: true); return Results.NoContent(); });
        cart.MapPatch("/items/{variantId:guid}", async (Guid variantId, QuantityRequest r, HttpContext c, CartService s) => { await s.SetAsync(CartService.KeyFor(c), variantId, r.Quantity, add: false); return Results.NoContent(); });
        cart.MapDelete("/items/{variantId:guid}", async (Guid variantId, HttpContext c, CartService s) => { await s.SetAsync(CartService.KeyFor(c), variantId, 0, add: false); return Results.NoContent(); });
        cart.MapPost("/merge", async (MergeRequest r, HttpContext c, CartService s) =>
        { await s.MergeAsync(r.GuestId, c.User.UserId()); return Results.NoContent(); });    // UserId() throws 401 when the caller is anonymous — merging needs a real user

        // ───── ORDERS ─────
        var g = app.MapGroup("/api/v1/orders").WithTags("Orders").RequireAuthorization();

        g.MapPost("/", async (PlaceOrderRequest r, HttpContext c, CheckoutService svc, CancellationToken ct) =>
        {
            if (!c.User.IsFullyVerified()) throw new ForbiddenException("VERIFICATION_REQUIRED", "Verify your email and phone before ordering.");
            if (!c.User.IsInRole(Roles.Customer)) throw new ForbiddenException("CUSTOMER_ONLY", "Only customer accounts can place orders.");
            if (!Guid.TryParse(c.Request.Headers["Idempotency-Key"].FirstOrDefault(), out var key))
                throw new BusinessRuleException("IDEMPOTENCY_KEY_REQUIRED", "Send an Idempotency-Key header containing a GUID.");
            var result = await svc.PlaceOrderAsync(c.User.UserId(), r, key.ToString("N"), ct);
            return Results.Created($"/api/v1/orders/{result.OrderId}", result);
        }).WithValidation<PlaceOrderRequest>();

        g.MapGet("/", async (HttpContext c, OrdersDb db, int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = Paging.Normalize(page, pageSize);
            var uid = c.User.UserId();
            var q = db.Orders.AsNoTracking().Where(o => o.CustomerId == uid);                       // ALWAYS filtered by the token's user (admin listing is a Phase 5 feature)
            var total = await q.LongCountAsync(ct);
            var items = await q.OrderByDescending(o => o.CreatedAt).Skip((p - 1) * size).Take(size)
                .Select(o => new { o.Id, o.OrderNumber, status = o.Status.ToString(), o.TotalCents, o.Currency, o.CreatedAt, itemCount = o.Items.Sum(i => i.Quantity) }).ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(items.Cast<object>().ToList(), p, size, total));
        });

        g.MapGet("/{id:guid}", async (Guid id, HttpContext c, OrdersDb db, CancellationToken ct) =>
        {
            var o = await LoadAuthorizedAsync(db, id, c, ct);
            return Results.Ok(new
            {
                o.Id, o.OrderNumber, status = o.Status.ToString(), o.SubtotalCents, o.DeliveryFeeCents, o.DiscountCents, o.TotalCents, o.Currency, o.PaymentId, o.PaymentExpiresAt, o.CreatedAt, o.CancelReason,
                deliveryAddress = o.DeliveryAddress,
                items = o.Items.Select(i => new { i.VariantId, i.ProductId, name = i.ProductNameSnapshot, i.Sku, imageUrl = i.ImageUrlSnapshot, i.Quantity, i.UnitPriceCents, lineTotalCents = i.LineTotalCents }),
                shipments = o.Shipments.Select(s => new { s.Id, s.SellerId, status = s.Status.ToString() })
            });
        });

        g.MapGet("/{id:guid}/tracking", async (Guid id, HttpContext c, OrdersDb db, CancellationToken ct) =>
        {
            var o = await LoadAuthorizedAsync(db, id, c, ct);
            return Results.Ok(new { o.OrderNumber, status = o.Status.ToString(), timeline = o.Events.OrderBy(e => e.At).Select(e => new { status = e.ToStatus.ToString(), e.Note, at = e.At }) });
        });

        g.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext c, OrdersDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            var o = await LoadAuthorizedAsync(db, id, c, ct);
            if (o.CustomerId != c.User.UserId()) throw new NotFoundException();
            var now = clock.GetUtcNow();
            // Phase 1: customers can cancel only BEFORE paying. Cancelling a paid order needs the refund flow (Phase 5).
            if (!await OrderTransitions.TryAsync(db, o.Id, OrderStatus.PendingPayment, OrderStatus.Cancelled, "customer", "Cancelled by customer", now, "CUSTOMER_CANCELLED"))
                throw new BusinessRuleException("CANNOT_CANCEL", "Only orders awaiting payment can be cancelled right now.");
            await bus.Publish(new OrderCancelled(o.Id, o.CustomerId, "CUSTOMER_CANCELLED", CheckoutService.ToLines(o), now), ct);
            return Results.NoContent();
        });

        // ───── DEV SIMULATOR: stands in for the Logistics service until Phase 4 ─────
        if (app.Environment.IsDevelopment())
            app.MapPost("/api/v1/dev/orders/{id:guid}/advance", async (Guid id, OrdersDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
            {
                var o = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
                var next = OrderStateMachine.Next(o.Status) ?? throw new BusinessRuleException("NO_NEXT_STATE", $"No next step from {o.Status}.");
                var now = clock.GetUtcNow();
                await OrderTransitions.TryAsync(db, id, o.Status, next, "dev-simulator", "Simulated logistics step", now);
                await bus.Publish(new OrderStatusChanged(id, o.CustomerId, o.Status.ToString(), next.ToString(), now), ct);
                return Results.Ok(new { status = next.ToString() });
            }).RequireAuthorization(Policies.SuperAdmin);

        static async Task<Order> LoadAuthorizedAsync(OrdersDb db, Guid id, HttpContext c, CancellationToken ct)
        {
            var o = await db.Orders.AsNoTracking().Include(x => x.Items).Include(x => x.Shipments).Include(x => x.Events).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
            // CanView policy (architecture doc §4.1): owner OR a seller who has items in it OR an authorised staff role. Anything else → 404, not 403 (don't confirm the order exists).
            var uid = c.User.UserId();
            var allowed = o is not null && (o.CustomerId == uid || o.Shipments.Any(s => s.SellerId == uid) || IsAdmin(c.User));
            return allowed ? o! : throw new NotFoundException();
        }
    }
}
```
`Program.cs`
```csharp
using FluentValidation;
using LankaMart.Orders.Api.Clients;
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Features;
using LankaMart.Orders.Api.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("orders");

builder.Services.AddDbContext<OrdersDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "orders"))
    .UseSnakeCaseNamingConvention());
builder.Services.Configure<OrdersOptions>(cfg.GetSection("Orders"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInternalClient<ICatalogClient, CatalogClient>(cfg, "Catalog");
builder.Services.AddInternalClient<IInventoryClient, InventoryClient>(cfg, "Inventory");
builder.Services.AddInternalClient<IPaymentsClient, PaymentsClient>(cfg, "Payments");
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddHostedService<ExpireStaleOrdersWorker>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformRedis(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "orders", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
await app.MigrateDatabaseAsync<OrdersDb>();
app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapOrders();
app.Run();

public partial class Program;
```
```bash
dotnet ef migrations add Initial --project src/services/orders/LankaMart.Orders.Api --output-dir Data/Migrations
```

✅ **Check (cart + order creation *without* Payments yet)**
1. Guest: `POST /api/v1/cart/items` with header `X-Guest-Id: <any-guid>` → `GET /cart` returns name/price from Catalog. Add the same item twice → quantity 2; add 20 → capped at 10.
2. Log in, `POST /cart/merge {guestId}` → the guest items appear in the user's cart.
3. `POST /orders` **without** `Idempotency-Key` → 422. Unverified user → 403. With both → Payments isn't running yet, so you should get **503 `PAYMENT_UNAVAILABLE`** — and then verify the **compensation worked**: the order is `Cancelled` (`orders.orders`), an `OrderCancelled` event was handled, the Inventory reservation is `Released`, Redis stock restored. *You just watched a saga compensate.*
4. Order a quantity larger than stock → 409 `OUT_OF_STOCK`, order `Cancelled`, nothing reserved.
5. Fire the same `POST /orders` **twice with the same key** (and once concurrently) → one order only.
6. Another user `GET /orders/{id}` → **404**.
7. Unit-test `OrderStateMachine` right now (§1.16) — it's pure logic and the best first test you'll write.

⚠️ **Pitfalls:** forgetting `AsNoTracking()` on read endpoints (slower, tracks everything) · using `Include` on 3+ collections without `AsSplitQuery()` (cartesian explosion → huge slow queries) · calling external services **inside** a DB transaction (a slow Payment would hold your DB transaction open — the saga deliberately commits first, calls out second) · `ExecuteUpdateAsync` bypasses the change tracker: after it, an already-loaded entity is stale, so re-query if you need fresh state.

Commit: `feat(orders): cart, checkout saga, state machine, tracking`.

---

## 1.12 Payment service (with the fake gateway)

**Port 5106 · schema `payments` · login `payments_svc`.**
**Mission:** *money in.* Create payment intents, run the **fake gateway** that behaves like a real one (asynchronous, signed webhooks), record every movement in an **append-only double-entry ledger**, and tell the world `PaymentCompleted` / `PaymentFailed`.

🧠 **Why a fake gateway is architecturally correct** (System Scenarios §2.3): everything downstream — webhook verification, idempotency, ledger, events, order updates — runs **for real** against fake money. Swapping in PayHere later means writing **one adapter class**; Orders, Inventory and the UI don't change. The mock must therefore behave like a real gateway: *you submit a card, you get "accepted for processing", and the final result arrives later by a server-to-server webhook.*

**Fake card rules (deterministic, so tests are repeatable):**

| Input | Result |
|---|---|
| card `4111 1111 1111 1111` (or any other Luhn-valid number) | ✅ **SUCCESS** after ~2 s |
| card `4000 0000 0000 0002` | ❌ **DECLINED** (`card_declined`) |
| card `4000 0000 0000 0119` | ❌ **GATEWAY ERROR** (`gateway_error`) |
| order total whose cents end in **`.99`** (e.g. LKR 1,499.99) | ⏳ **PENDING** — webhook arrives after **30 s**, then success |
| Luhn-invalid number / expired date / bad CVC | rejected immediately with 422 (never reaches the "gateway") |

**Endpoints**

| Method & path | Who | Purpose |
|---|---|---|
| `POST /api/v1/internal/payments/intent` | Orders (internal key) | create/return the payment for an order (idempotent on `orderId`) |
| `GET /api/v1/payments/{id}` | the paying customer | status (the UI polls this) |
| `POST /api/v1/payments/{id}/pay` | the paying customer | submit the fake card → `202 Accepted` |
| `POST /api/v1/webhooks/mock` | the "gateway" (HMAC-signed) | delivers the final result |
| `POST /api/v1/dev/mock/{paymentId}/duplicate-webhook · bad-signature · amount-mismatch` | dev only | **failure lab** (System Scenarios §7) |

### 1.12.1 Money rules (read before coding)
1. **Integer cents** everywhere. 2. **The ledger is append-only** — rows are never updated or deleted; mistakes are corrected with *reversing entries*. 3. **Every transaction balances:** `SUM(debits) = SUM(credits)`. 4. **Never store or log card numbers/CVC** — the fake gateway inspects them in memory and forgets them. (Real PCI-DSS rules are why real systems send customers to the gateway's hosted page instead of touching cards at all.) 5. **A webhook is untrusted input** until its signature verifies, and it may arrive **more than once**.

### 1.12.2 Domain & DB

`Domain/Entities.cs`
```csharp
namespace LankaMart.Payments.Api.Domain;

public enum PaymentStatus { Created, Processing, Succeeded, Failed, RefundRequired }

public class Payment
{
    public Guid Id { get; set; } public Guid OrderId { get; set; } public Guid CustomerId { get; set; }
    public long AmountCents { get; set; } public string Currency { get; set; } = "LKR";
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    public string Gateway { get; set; } = "mock"; public string? GatewayRef { get; set; } public string? FailureCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One business event that moves money (CHARGE now; REFUND, COMMISSION, PAYOUT in later phases). Contains balanced entries.</summary>
public class LedgerTransaction
{
    public Guid Id { get; set; } public Guid PaymentId { get; set; } public Guid OrderId { get; set; }
    public string Type { get; set; } = "CHARGE"; public long AmountCents { get; set; } public string Currency { get; set; } = "LKR";
    public string GatewayRef { get; set; } = ""; public string IdempotencyKey { get; set; } = "";   // UNIQUE → the same charge can never be booked twice
    public DateTimeOffset CreatedAt { get; set; }
    public List<LedgerEntry> Entries { get; set; } = [];
}
public class LedgerEntry
{
    public Guid Id { get; set; } public Guid TransactionId { get; set; }
    public string Account { get; set; } = "";                // gateway_receivable | escrow | seller:{id} | platform_revenue | gateway_fees
    public long DebitCents { get; set; } public long CreditCents { get; set; } public DateTimeOffset CreatedAt { get; set; }
}
public class WebhookEvent        // raw evidence, kept 90 days for disputes (architecture doc §4.6)
{
    public Guid Id { get; set; } public string Gateway { get; set; } = ""; public Guid GatewayEventId { get; set; }
    public string Type { get; set; } = ""; public string RawPayload { get; set; } = ""; public DateTimeOffset ReceivedAt { get; set; }
}
```

`Data/PaymentsDb.cs`
```csharp
using LankaMart.Payments.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace LankaMart.Payments.Api.Data;

public class PaymentsDb(DbContextOptions<PaymentsDb> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<LedgerTransaction> Transactions => Set<LedgerTransaction>();
    public DbSet<LedgerEntry> Entries => Set<LedgerEntry>();
    public DbSet<WebhookEvent> Webhooks => Set<WebhookEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("payments");
        b.Entity<Payment>(e => { e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.HasIndex(x => x.OrderId).IsUnique(); e.HasIndex(x => x.CustomerId); });
        b.Entity<LedgerTransaction>(e => { e.HasIndex(x => x.IdempotencyKey).IsUnique(); e.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.TransactionId); });
        b.Entity<LedgerEntry>().ToTable(t => t.HasCheckConstraint("ck_entry_one_side",
            "(debit_cents > 0 AND credit_cents = 0) OR (credit_cents > 0 AND debit_cents = 0)"));      // every entry is exactly one of debit/credit
        b.Entity<WebhookEvent>().HasIndex(x => new { x.Gateway, x.GatewayEventId }).IsUnique();         // dedupe: a re-sent webhook can't be processed twice
    }
}
```
*(Phase 6 hardening: revoke `UPDATE`/`DELETE` on `ledger_*` tables from the service login so even a bug can't rewrite history.)*

### 1.12.3 The fake gateway

`Gateway/Mock.cs`
```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace LankaMart.Payments.Api.Gateway;

public sealed class MockOptions { public string WebhookSecret { get; set; } = ""; public string WebhookUrl { get; set; } = ""; public int NormalDelaySeconds { get; set; } = 2; public int PendingDelaySeconds { get; set; } = 30; }

public sealed record CardInput(string Number, int ExpMonth, int ExpYear, string Cvc, string Holder)
{ public override string ToString() => "CardInput(redacted)"; }          // a record's default ToString prints every field — never let a card reach a log line

public enum MockOutcome { Success, Declined, GatewayError, PendingThenSuccess }

public static class MockRules
{
    public static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    public static bool Luhn(string number)      // the checksum every real card number satisfies (catches typos)
    {
        var d = Digits(number); if (d.Length is < 12 or > 19) return false;
        var sum = 0; var alt = false;
        for (var i = d.Length - 1; i >= 0; i--) { var n = d[i] - '0'; if (alt) { n *= 2; if (n > 9) n -= 9; } sum += n; alt = !alt; }
        return sum % 10 == 0;
    }

    public static MockOutcome Decide(string cardNumber, long amountCents)
    {
        var n = Digits(cardNumber);
        if (n == "4000000000000002") return MockOutcome.Declined;
        if (n == "4000000000000119") return MockOutcome.GatewayError;
        if (amountCents % 100 == 99) return MockOutcome.PendingThenSuccess;        // "any amount ending .99 → PENDING"
        return MockOutcome.Success;
    }
}

public sealed record MockWebhook(Guid EventId, string Type, string GatewayRef, Guid PaymentId, long AmountCents, string? FailureCode, DateTimeOffset OccurredAt);
public sealed record MockJob(MockWebhook Event, TimeSpan Delay);

public static class MockJson { public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web); }

public static class MockSigner
{
    public static string Sign(string body, string secret) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    public static bool Verify(string body, string signature, string secret) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(body, secret)), Encoding.UTF8.GetBytes(signature ?? ""));       // constant-time compare
}

/// <summary>The "bank": decides the outcome and schedules the webhook. Real adapters (PayHere) will implement the same idea: accept → later notify.</summary>
public sealed class MockGateway(Channel<MockJob> queue, IOptions<MockOptions> opt, TimeProvider clock)
{
    public async Task ChargeAsync(Guid paymentId, long amountCents, CardInput card, CancellationToken ct)
    {
        var outcome = MockRules.Decide(card.Number, amountCents);
        var gatewayRef = $"mock_{Guid.NewGuid():N}";
        var normal = TimeSpan.FromSeconds(opt.Value.NormalDelaySeconds); var slow = TimeSpan.FromSeconds(opt.Value.PendingDelaySeconds);
        var (type, code, delay) = outcome switch
        {
            MockOutcome.Declined => ("charge.failed", "card_declined", normal),
            MockOutcome.GatewayError => ("charge.failed", "gateway_error", normal),
            MockOutcome.PendingThenSuccess => ("charge.succeeded", (string?)null, slow),
            _ => ("charge.succeeded", (string?)null, normal)
        };
        await queue.Writer.WriteAsync(new MockJob(new MockWebhook(Guid.NewGuid(), type, gatewayRef, paymentId, amountCents, code, clock.GetUtcNow()), delay), ct);
    }
}

/// <summary>Delivers webhooks over real HTTP with a real HMAC signature (so the receiving code path is genuine).</summary>
public sealed class MockWebhookSender(IHttpClientFactory http, IOptions<MockOptions> opt, ILogger<MockWebhookSender> log)
{
    public static readonly ConcurrentDictionary<Guid, MockWebhook> LastSent = new();    // paymentId → last webhook (used by the failure lab)

    public async Task<int> SendAsync(MockWebhook evt, bool validSignature = true)
    {
        var body = JsonSerializer.Serialize(evt, MockJson.Options);
        var sig = MockSigner.Sign(body, validSignature ? opt.Value.WebhookSecret : "wrong-secret");
        using var req = new HttpRequestMessage(HttpMethod.Post, opt.Value.WebhookUrl) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Add("X-Mock-Signature", sig);
        LastSent[evt.PaymentId] = evt;
        var res = await http.CreateClient().SendAsync(req);
        log.LogInformation("Mock webhook {Type} for payment {PaymentId} → HTTP {Status}", evt.Type, evt.PaymentId, (int)res.StatusCode);
        return (int)res.StatusCode;     // a real gateway would retry non-2xx with backoff; the mock just logs
    }
}

public sealed class MockWebhookDispatcher(Channel<MockJob> queue, MockWebhookSender sender, ILogger<MockWebhookDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stop))
            _ = Task.Run(async () =>                       // each job waits independently, so a 30 s pending payment doesn't block the others
            {
                try { await Task.Delay(job.Delay, stop); await sender.SendAsync(job.Event); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Mock webhook delivery failed"); }
            }, stop);
    }
}
```
*(Jobs live in memory: restarting Payments loses scheduled webhooks. That's acceptable for a mock; a real gateway keeps retrying on its side.)*

### 1.12.4 The payment logic and webhook processor

`Features/PaymentService.cs`
```csharp
using System.Text.Json;
using FluentValidation;
using LankaMart.Payments.Api.Data;
using LankaMart.Payments.Api.Domain;
using LankaMart.Payments.Api.Gateway;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LankaMart.Payments.Api.Features;

public sealed record PayRequest(string CardNumber, int ExpMonth, int ExpYear, string Cvc, string CardHolder);
public sealed class PayValidator : AbstractValidator<PayRequest>
{
    public PayValidator()
    {
        RuleFor(x => x.CardNumber).Must(MockRules.Luhn).WithMessage("Card number is invalid.").WithErrorCode("INVALID_CARD");
        RuleFor(x => x.ExpMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.ExpYear).InclusiveBetween(DateTime.UtcNow.Year, DateTime.UtcNow.Year + 20);
        RuleFor(x => x).Must(x => new DateTime(x.ExpYear, x.ExpMonth, 1).AddMonths(1) > DateTime.UtcNow).WithMessage("Card has expired.").When(x => x.ExpMonth is >= 1 and <= 12);
        RuleFor(x => x.Cvc).Matches(@"^\d{3,4}$");
        RuleFor(x => x.CardHolder).Length(2, 80);
    }
}

public static class Db { public static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: "23505" }; }

public sealed class PaymentService(PaymentsDb db, MockGateway gateway, TimeProvider clock)
{
    public async Task<CreatePaymentIntentResponse> CreateIntentAsync(CreatePaymentIntentRequest r, CancellationToken ct)
    {
        if (r.AmountCents <= 0) throw new BusinessRuleException("INVALID_AMOUNT", "Amount must be positive.");
        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == r.OrderId, ct);
        if (existing is not null) return new(existing.Id, existing.Gateway);                       // idempotent: Orders may retry this call after a timeout

        var now = clock.GetUtcNow();
        var p = new Payment { Id = Guid.CreateVersion7(), OrderId = r.OrderId, CustomerId = r.CustomerId, AmountCents = r.AmountCents, Currency = r.Currency, CreatedAt = now, UpdatedAt = now };
        db.Payments.Add(p);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (Db.IsUniqueViolation(ex))                                // two concurrent calls: the unique index on order_id lets exactly one win
        { existing = await db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == r.OrderId, ct); return new(existing.Id, existing.Gateway); }
        return new(p.Id, p.Gateway);
    }

    public async Task PayAsync(Guid paymentId, Guid userId, PayRequest card, CancellationToken ct)
    {
        var p = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == paymentId && x.CustomerId == userId, ct) ?? throw new NotFoundException();   // owner only (IDOR)
        var now = clock.GetUtcNow();
        // ATOMIC CLAIM: Created → Processing. A double-click or a replayed request flips 0 rows and is refused.
        var claimed = await db.Payments.Where(x => x.Id == paymentId && x.Status == PaymentStatus.Created)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PaymentStatus.Processing).SetProperty(x => x.UpdatedAt, now), ct);
        if (claimed == 0)
            throw p.Status switch
            {
                PaymentStatus.Succeeded => new ConflictException("ALREADY_PAID", "This order is already paid."),
                PaymentStatus.Failed => new ConflictException("PAYMENT_FAILED", "This payment failed. Please place a new order."),
                _ => new ConflictException("PAYMENT_IN_PROGRESS", "Your payment is being processed.")
            };
        await gateway.ChargeAsync(p.Id, p.AmountCents, new CardInput(card.CardNumber, card.ExpMonth, card.ExpYear, card.Cvc, card.CardHolder), ct);   // card lives in memory only
    }
}

public sealed class WebhookProcessor(PaymentsDb db, IPublishEndpoint bus, IOptions<MockOptions> opt, TimeProvider clock, ILogger<WebhookProcessor> log)
{
    public async Task HandleAsync(string rawBody, string signature, CancellationToken ct)
    {
        // 1) AUTHENTICITY: HMAC over the RAW body (not re-serialised JSON — that could differ by a space and break the signature)
        if (!MockSigner.Verify(rawBody, signature, opt.Value.WebhookSecret))
        { log.LogWarning("Webhook rejected: bad signature"); throw new UnauthorizedException("INVALID_SIGNATURE", "Invalid signature"); }

        var evt = JsonSerializer.Deserialize<MockWebhook>(rawBody, MockJson.Options) ?? throw new BusinessRuleException("BAD_PAYLOAD", "Unreadable payload");

        // 2) IDEMPOTENCY: gateways retry. Seen this event id before? → acknowledge and do nothing.
        if (await db.Webhooks.AnyAsync(w => w.Gateway == "mock" && w.GatewayEventId == evt.EventId, ct)) return;

        var p = await db.Payments.SingleOrDefaultAsync(x => x.Id == evt.PaymentId, ct);
        if (p is null) { log.LogWarning("Webhook for unknown payment {PaymentId}", evt.PaymentId); return; }

        // 3) INTEGRITY: the amount in the webhook must equal what WE asked to charge
        if (evt.AmountCents != p.AmountCents)
        { log.LogError("AMOUNT MISMATCH on payment {PaymentId}: expected {Expected}, webhook says {Got}", p.Id, p.AmountCents, evt.AmountCents); throw new BusinessRuleException("AMOUNT_MISMATCH", "Amount mismatch"); }

        var now = clock.GetUtcNow();
        db.Webhooks.Add(new WebhookEvent { Id = Guid.CreateVersion7(), Gateway = "mock", GatewayEventId = evt.EventId, Type = evt.Type, RawPayload = rawBody, ReceivedAt = now });

        object? toPublish = null;
        if (evt.Type == "charge.succeeded" && p.Status is PaymentStatus.Processing or PaymentStatus.Created)
        {
            p.Status = PaymentStatus.Succeeded; p.GatewayRef = evt.GatewayRef; p.UpdatedAt = now;
            var tx = new LedgerTransaction { Id = Guid.CreateVersion7(), PaymentId = p.Id, OrderId = p.OrderId, Type = "CHARGE", AmountCents = p.AmountCents, Currency = p.Currency,
                                             GatewayRef = evt.GatewayRef, IdempotencyKey = $"charge:{evt.GatewayRef}", CreatedAt = now };
            // Double entry: the gateway owes us the money (asset ↑ = DEBIT) and we hold it in escrow for the sellers (liability ↑ = CREDIT)
            tx.Entries.Add(new LedgerEntry { Id = Guid.CreateVersion7(), TransactionId = tx.Id, Account = "gateway_receivable", DebitCents = p.AmountCents, CreatedAt = now });
            tx.Entries.Add(new LedgerEntry { Id = Guid.CreateVersion7(), TransactionId = tx.Id, Account = "escrow", CreditCents = p.AmountCents, CreatedAt = now });
            db.Transactions.Add(tx);
            toPublish = new PaymentCompleted(p.Id, p.OrderId, p.CustomerId, p.AmountCents, p.Currency, evt.GatewayRef, now);
        }
        else if (evt.Type == "charge.failed" && p.Status is PaymentStatus.Processing or PaymentStatus.Created)
        {
            p.Status = PaymentStatus.Failed; p.FailureCode = evt.FailureCode ?? "unknown"; p.UpdatedAt = now;
            toPublish = new PaymentFailed(p.Id, p.OrderId, p.CustomerId, p.FailureCode, now);
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (Db.IsUniqueViolation(ex)) { return; }        // two identical webhooks raced; the unique index let exactly one through
        if (toPublish is not null) await bus.Publish(toPublish, ct);                  // ⚠ dual-write (DB then event) — Phase 2 outbox fixes it
    }
}
```
🧠 **Why `PaymentCompleted` is published *after* the commit:** consumers must never see an event about data that isn't durably saved. The cost (if we crash right after the commit, the event is lost) is exactly what the Phase 2 **outbox** removes. Today's safety net: re-sending the same webhook is harmless (idempotent) — and your *failure lab* can prove it.

### 1.12.5 Endpoints, consumer, `Program.cs`

`Features/Endpoints.cs`
```csharp
using LankaMart.Payments.Api.Data;
using LankaMart.Payments.Api.Domain;
using LankaMart.Payments.Api.Gateway;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LankaMart.Payments.Api.Features;

public static class Endpoints
{
    public static void MapPayments(this WebApplication app)
    {
        // INTERNAL (Orders → Payments)
        app.MapInternalGroup("/api/v1/internal/payments")
           .MapPost("/intent", async (CreatePaymentIntentRequest r, PaymentService s, CancellationToken ct) => Results.Ok(await s.CreateIntentAsync(r, ct)));

        // CUSTOMER
        var g = app.MapGroup("/api/v1/payments").WithTags("Payments").RequireAuthorization();
        g.MapGet("/{id:guid}", async (Guid id, HttpContext c, PaymentsDb db, CancellationToken ct) =>
        {
            var p = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == c.User.UserId(), ct) ?? throw new NotFoundException();
            return Results.Ok(new { p.Id, p.OrderId, status = p.Status.ToString(), p.AmountCents, p.Currency, p.Gateway, method = "card_form", p.FailureCode });
        });
        g.MapPost("/{id:guid}/pay", async (Guid id, PayRequest r, HttpContext c, PaymentService s, CancellationToken ct) =>
        { await s.PayAsync(id, c.User.UserId(), r, ct); return Results.Accepted(value: new { status = "processing" }); }).WithValidation<PayRequest>();

        // WEBHOOK (public on purpose; protected by the HMAC signature, not by a JWT)
        app.MapPost("/api/v1/webhooks/mock", async (HttpRequest req, WebhookProcessor p, CancellationToken ct) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct);                  // RAW body, needed to verify the signature
            await p.HandleAsync(body, req.Headers["X-Mock-Signature"].ToString(), ct);
            return Results.Ok();                                         // fast 200 so the gateway stops retrying
        }).AllowAnonymous();

        // FAILURE LAB (dev only) — System Scenarios §7: "test your saga compensations against every one of these weekly"
        if (app.Environment.IsDevelopment())
        {
            var lab = app.MapGroup("/api/v1/dev/mock").AllowAnonymous();
            lab.MapPost("/{paymentId:guid}/duplicate-webhook", async (Guid paymentId, MockWebhookSender s) =>
                MockWebhookSender.LastSent.TryGetValue(paymentId, out var last) ? Results.Ok(new { httpStatus = await s.SendAsync(last) }) : Results.NotFound());   // SAME event id → must be a harmless no-op
            lab.MapPost("/{paymentId:guid}/bad-signature", async (Guid paymentId, MockWebhookSender s) =>
                MockWebhookSender.LastSent.TryGetValue(paymentId, out var last) ? Results.Ok(new { httpStatus = await s.SendAsync(last with { EventId = Guid.NewGuid() }, validSignature: false) }) : Results.NotFound());   // must be 401
            lab.MapPost("/{paymentId:guid}/amount-mismatch", async (Guid paymentId, MockWebhookSender s) =>
                MockWebhookSender.LastSent.TryGetValue(paymentId, out var last) ? Results.Ok(new { httpStatus = await s.SendAsync(last with { EventId = Guid.NewGuid(), AmountCents = 1 }) }) : Results.NotFound());   // must be 422
        }
    }
}

public sealed class OrderPaymentOrphanedConsumer(PaymentsDb db, ILogger<OrderPaymentOrphanedConsumer> log) : IConsumer<OrderPaymentOrphaned>
{
    public async Task Consume(ConsumeContext<OrderPaymentOrphaned> ctx)
    {   // money was taken for an order that no longer exists → flag for the refund flow (Phase 5)
        var n = await db.Payments.Where(p => p.Id == ctx.Message.PaymentId && p.Status == PaymentStatus.Succeeded)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.RefundRequired));
        if (n > 0) log.LogError("Payment {PaymentId} needs a REFUND (order {OrderId} no longer valid)", ctx.Message.PaymentId, ctx.Message.OrderId);
    }
}
```

`Program.cs`
```csharp
using System.Threading.Channels;
using FluentValidation;
using LankaMart.Payments.Api.Data;
using LankaMart.Payments.Api.Features;
using LankaMart.Payments.Api.Gateway;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("payments");

builder.Services.AddDbContext<PaymentsDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "payments"))
    .UseSnakeCaseNamingConvention());
builder.Services.Configure<MockOptions>(cfg.GetSection("Mock"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(Channel.CreateUnbounded<MockJob>());
builder.Services.AddSingleton<MockGateway>();
builder.Services.AddSingleton<MockWebhookSender>();
builder.Services.AddHostedService<MockWebhookDispatcher>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<WebhookProcessor>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "payments", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
await app.MigrateDatabaseAsync<PaymentsDb>();
app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapPayments();
app.Run();

public partial class Program;
```
Dev config extra (`appsettings.Development.json`):
```json
"Mock": { "WebhookSecret": "dev-mock-webhook-secret-change-me", "WebhookUrl": "http://localhost:5106/api/v1/webhooks/mock", "NormalDelaySeconds": 2, "PendingDelaySeconds": 30 }
```
```bash
dotnet ef migrations add Initial --project src/services/payments/LankaMart.Payments.Api --output-dir Data/Migrations
```

**Ledger self-check query** — run it any time; both numbers must always match:
```sql
SELECT SUM(debit_cents) AS debits, SUM(credit_cents) AS credits, SUM(debit_cents) - SUM(credit_cents) AS difference FROM payments.ledger_entries;
SELECT transaction_id, SUM(debit_cents) - SUM(credit_cents) AS unbalanced FROM payments.ledger_entries GROUP BY transaction_id HAVING SUM(debit_cents) <> SUM(credit_cents);  -- must return ZERO rows
```

✅ **Check — the full happy path and every failure path (all services running)**
1. Verified customer, address, cart with an item → `POST /orders` (+ `Idempotency-Key`) → 201 `{orderId, paymentId, totalCents}`. Order is `PendingPayment`, stock reserved.
2. `POST /payments/{paymentId}/pay` with `4111111111111111`, `12/2030`, `123` → **202**. Two seconds later in the logs: *webhook → payments → `PaymentCompleted` → orders `PaymentCompletedConsumer` → `OrderPaid` → inventory confirms.* Verify: order `Paid`, `inventory_items.qty_on_hand` down, `qty_reserved` back to 0, ledger has 2 balanced rows. **Open Jaeger** (`:16686`) and look at the trace of the `POST /orders` call — you can *see* the services involved.
3. **Decline:** card `4000000000000002` → order `PaymentFailed`, reservation `Released`, stock restored, **cart restored**.
4. **Pending:** buy the seeded product **"DEMO Pending-Payment Test Item"** (LKR 1,149.99 + LKR 350 delivery = LKR 1,499.99, so the total's cents end in `.99`) → pay → status stays `Processing` for 30 s → then `Paid`. *(Create it with `seed_demo.py`, §1.17.2.)*
5. **Double-click:** call `…/pay` twice quickly → second gets **409 `PAYMENT_IN_PROGRESS`**.
6. **Duplicate webhook:** `POST :5100/api/v1/dev/mock/{paymentId}/duplicate-webhook` → 200, **nothing changes** (one ledger transaction only).
7. **Bad signature:** `…/bad-signature` → **401**, state unchanged. **Amount mismatch:** `…/amount-mismatch` → **422**.
8. **Timeout:** create an order and **don't pay**; wait 15 min (or temporarily set `Orders:PaymentWindowMinutes` to 1) → `Expired`, reservation released (Inventory sweeper/event), stock back.
9. **Chaos test:** stop the Payments service, place an order → 503 `PAYMENT_UNAVAILABLE`, stock released, order `Cancelled`. Start Payments again → next order works. (Orders' circuit breaker will "open" after repeated failures — watch the log message.)
10. Another user calls `GET /payments/{id}` / `…/pay` → **404**.

⚠️ **Pitfalls:** signature mismatches almost always mean you verified a *re-serialised* body instead of the raw one · in `p.Status is PaymentStatus.Processing or PaymentStatus.Created` the `or` is a C# *pattern* combinator ("either value"), not the boolean `||` — it reads naturally but don't mix it with `&&` without parentheses when you add more conditions · never "fix" a wrong ledger row with `UPDATE` — add a reversing entry · if the order's total ends in `.99` only because of delivery fees, remember the *charged* amount is the **order total**.

Commit: `feat(payments): fake gateway, signed webhooks, double-entry ledger`.

> **🏁 BACKEND MILESTONE.** If steps 1–10 pass, the entire back-end of Phase 1 works. Tag it: `git tag phase-1-backend && git push --tags`. Everything from here is *clients* (React, Flutter), the Python service, tests, and hardening.

---

## 1.13 React storefront (web)

**Port 5173 (Vite dev server) · talks only to the gateway.**
🎯 **Goal:** a working storefront: browse/search → product page → cart (guest or logged in) → register/verify/login → checkout → fake payment → order list & tracking.

🧠 **Design decisions**
* **Vite + React + TypeScript SPA.** (SSR for SEO arrives in Phase 3; because the backend is a pure API, that move doesn't touch it.)
* **Vite dev proxy**: the browser calls `/api/...` on `localhost:5173`, Vite forwards to the gateway at `:5100`. No CORS problems in development, and the code uses *relative* URLs — identical to production, where Cloudflare serves site and API from one domain.
* **TanStack Query** manages server data (caching, loading/error states, refetching); **React Router** for pages; **React Hook Form + Zod** for forms/validation; **Tailwind CSS** for styling; **react-i18next** so *no visible text is hard-coded* (Phase 3 only adds si/ta files).
* **Never trust the client for money.** The UI *displays* prices from the API; it never sends them. Validation in React is for *user experience*, validation in the API is for *security* — you need both.

### 1.13.1 Setup

```bash
cd src/clients
npm create vite@latest storefront-web -- --template react-ts
cd storefront-web
npm install
npm install react-router @tanstack/react-query axios react-hook-form zod @hookform/resolvers i18next react-i18next
npm install tailwindcss @tailwindcss/vite
```
`vite.config.ts`:
```ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { port: 5173, proxy: { "/api": { target: "http://localhost:5100", changeOrigin: true } } },
});
```
`src/index.css`: first line `@import "tailwindcss";`

Folder structure:
```
src/
├── lib/        http.ts (axios + token refresh)  money.ts  i18n.ts
├── auth/       AuthContext.tsx  RequireAuth.tsx
├── api/        products.ts  cart.ts  orders.ts  payments.ts  (typed functions + hooks)
├── components/ Layout.tsx  ProductCard.tsx  QuantityInput.tsx  ErrorBanner.tsx
├── pages/      Home  Product  Cart  Login  Register  Verify  Checkout  Orders  OrderDetail
├── locales/    en.json
└── main.tsx  App.tsx
```

### 1.13.2 The HTTP client — token refresh done right

`src/lib/http.ts`
```ts
import axios, { AxiosError, InternalAxiosRequestConfig } from "axios";

export const api = axios.create({ baseURL: "/api/v1", timeout: 15000 });

type Tokens = { accessToken: string; refreshToken: string };
const KEY = "lm.tokens";
// ⚠ localStorage is readable by any script on the page (XSS). Phase 1 accepts that for simplicity;
// Phase 6 moves the refresh token to an httpOnly cookie. It's on your pen-test list.
export const tokenStore = {
  get(): Tokens | null { try { return JSON.parse(localStorage.getItem(KEY) ?? "null"); } catch { return null; } },
  set(t: Tokens | null) { t ? localStorage.setItem(KEY, JSON.stringify(t)) : localStorage.removeItem(KEY); },
};

export function guestId(): string {            // identifies an anonymous visitor's cart
  let g = localStorage.getItem("lm.guest");
  if (!g) { g = crypto.randomUUID(); localStorage.setItem("lm.guest", g); }
  return g;
}

api.interceptors.request.use((cfg) => {
  const t = tokenStore.get();
  if (t) cfg.headers.Authorization = `Bearer ${t.accessToken}`;
  cfg.headers["X-Guest-Id"] = guestId();
  return cfg;
});

// SINGLE-FLIGHT REFRESH: if five requests fail with 401 at once, they all wait for ONE refresh call.
// (Two parallel refreshes would look like token reuse to the server and log the user out.)
let refreshing: Promise<Tokens | null> | null = null;
async function refresh(): Promise<Tokens | null> {
  const t = tokenStore.get();
  if (!t) return null;
  try {
    const { data } = await axios.post("/api/v1/auth/refresh", { refreshToken: t.refreshToken });
    const next = { accessToken: data.accessToken, refreshToken: data.refreshToken };
    tokenStore.set(next);
    return next;
  } catch { tokenStore.set(null); return null; }
}

const NO_RETRY = /\/auth\/(login|register|refresh|verify-otp|resend-otp)/;
api.interceptors.response.use(
  (r) => r,
  async (error: AxiosError) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined;
    if (error.response?.status === 401 && original && !original._retried && !NO_RETRY.test(original.url ?? "")) {
      original._retried = true;
      refreshing ??= refresh().finally(() => { refreshing = null; });
      const t = await refreshing;
      if (t) { original.headers.Authorization = `Bearer ${t.accessToken}`; return api(original); }
      window.dispatchEvent(new Event("lm:logout"));
    }
    return Promise.reject(error);
  },
);

export type ApiError = { status?: number; code?: string; title: string; fields?: Record<string, string[]> };
export function toApiError(e: unknown): ApiError {      // the backend always answers with Problem Details {title, code, errors?}
  const ax = e as AxiosError<{ title?: string; code?: string; errors?: Record<string, string[]> }>;
  return { status: ax.response?.status, code: ax.response?.data?.code, title: ax.response?.data?.title ?? ax.message, fields: ax.response?.data?.errors };
}
```

`src/lib/money.ts`:
```ts
// Money arrives as integer cents. Format ONLY for display.
export const lkr = (cents: number) =>
  new Intl.NumberFormat("en-LK", { style: "currency", currency: "LKR", minimumFractionDigits: 2 }).format(cents / 100);
```

### 1.13.3 Auth context

`src/auth/AuthContext.tsx`
```tsx
import { createContext, useCallback, useContext, useEffect, useState, ReactNode } from "react";
import { api, tokenStore, guestId } from "../lib/http";

export type Me = { userId: string; email: string; roles: string[]; emailVerified: boolean; phoneVerified: boolean; sellerId?: string };
type Ctx = { me: Me | null; loading: boolean; login(email: string, password: string): Promise<void>; logout(): Promise<void>; reload(): Promise<void> };
const AuthCtx = createContext<Ctx>(null!);
export const useAuth = () => useContext(AuthCtx);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => {
    if (!tokenStore.get()) { setMe(null); setLoading(false); return; }
    try { setMe((await api.get<Me>("/auth/me")).data); } catch { setMe(null); } finally { setLoading(false); }
  }, []);

  useEffect(() => { reload(); const f = () => setMe(null); window.addEventListener("lm:logout", f); return () => window.removeEventListener("lm:logout", f); }, [reload]);

  async function login(email: string, password: string) {
    const { data } = await api.post("/auth/login", { email, password });
    tokenStore.set({ accessToken: data.accessToken, refreshToken: data.refreshToken });
    await api.post("/cart/merge", { guestId: guestId() }).catch(() => {});     // fold the guest cart into the account cart
    await reload();
  }
  async function logout() {
    const t = tokenStore.get();
    if (t) await api.post("/auth/logout", { refreshToken: t.refreshToken }).catch(() => {});   // revoke on the server too
    tokenStore.set(null); setMe(null);
  }
  return <AuthCtx.Provider value={{ me, loading, login, logout, reload }}>{children}</AuthCtx.Provider>;
}
```

### 1.13.4 Data hooks

`src/api/products.ts`
```ts
import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/http";

export type ProductListItem = { id: string; slug: string; name: string; imageUrl?: string; priceFromCents: number; ratingAvg: number; ratingCount: number; sellerId: string };
export type Paged<T> = { items: T[]; page: number; pageSize: number; total: number; totalPages: number };

export const useProducts = (params: { query?: string; category?: string; sort?: string; page?: number }) =>
  useQuery({ queryKey: ["products", params], queryFn: async () =>
    (await api.get<Paged<ProductListItem>>("/products", { params: { ...params, page_size: 12 } })).data });

export const useProduct = (slug: string) =>
  useQuery({ queryKey: ["product", slug], queryFn: async () => (await api.get(`/products/${slug}`)).data });

export const useAvailability = (variantIds: string[]) =>
  useQuery({ queryKey: ["availability", variantIds], enabled: variantIds.length > 0,
    queryFn: async () => (await api.get<{ variantId: string; status: "in_stock" | "low_stock" | "out_of_stock" }[]>("/inventory/availability", { params: { variantIds: variantIds.join(",") } })).data });
```
`src/api/cart.ts`
```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../lib/http";

export type CartLine = { variantId: string; quantity: number; name?: string; imageUrl?: string; unitPriceCents: number; lineTotalCents: number; available: boolean };
export type Cart = { lines: CartLine[]; subtotalCents: number };

export const useCart = () => useQuery({ queryKey: ["cart"], queryFn: async () => (await api.get<Cart>("/cart")).data });

export function useCartActions() {
  const qc = useQueryClient();
  const refresh = () => qc.invalidateQueries({ queryKey: ["cart"] });
  return {
    add: useMutation({ mutationFn: (v: { variantId: string; quantity: number }) => api.post("/cart/items", v), onSuccess: refresh }),
    setQty: useMutation({ mutationFn: (v: { variantId: string; quantity: number }) => api.patch(`/cart/items/${v.variantId}`, { quantity: v.quantity }), onSuccess: refresh }),
    remove: useMutation({ mutationFn: (variantId: string) => api.delete(`/cart/items/${variantId}`), onSuccess: refresh }),
  };
}
```

### 1.13.5 Checkout + fake payment page (the important one)

`src/pages/CheckoutPage.tsx`
```tsx
import { useRef, useState } from "react";
import { useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, toApiError } from "../lib/http";
import { lkr } from "../lib/money";
import { useCart } from "../api/cart";
import { useTranslation } from "react-i18next";

type Address = { id: string; label: string; recipientName: string; phone: string; line1: string; line2?: string; city: string; district: string; postalCode?: string; isDefault: boolean };
type Placed = { orderId: string; orderNumber: string; totalCents: number; paymentId: string };
type PaymentStatus = { status: "Created" | "Processing" | "Succeeded" | "Failed" | "RefundRequired"; failureCode?: string };

export default function CheckoutPage() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const cart = useCart();
  const addresses = useQuery({ queryKey: ["addresses"], queryFn: async () => (await api.get<Address[]>("/customers/me/addresses")).data });
  const [addressId, setAddressId] = useState<string>();
  const [placed, setPlaced] = useState<Placed>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [phase, setPhase] = useState<"form" | "processing" | "declined">("form");

  // ONE idempotency key per checkout attempt. Network retry/double-click re-use it → server returns the SAME order.
  const idemKey = useRef(crypto.randomUUID());

  async function placeOrder() {
    const a = addresses.data?.find((x) => x.id === (addressId ?? addresses.data?.find((d) => d.isDefault)?.id));
    if (!a) return setError("Choose a delivery address.");
    setBusy(true); setError(undefined);
    try {
      const { data } = await api.post<Placed>("/orders",
        { deliveryAddress: { recipientName: a.recipientName, phone: a.phone, line1: a.line1, line2: a.line2, city: a.city, district: a.district, postalCode: a.postalCode } },
        { headers: { "Idempotency-Key": idemKey.current } });
      setPlaced(data);
    } catch (e) {
      const err = toApiError(e);
      if (err.code !== "REQUEST_IN_PROGRESS") idemKey.current = crypto.randomUUID();   // a FINAL failure (e.g. out of stock) → next attempt is a new attempt
      setError(err.title);
    } finally { setBusy(false); }
  }

  async function pay(card: { cardNumber: string; expMonth: number; expYear: number; cvc: string; cardHolder: string }) {
    if (!placed) return;
    setBusy(true); setError(undefined);
    try {
      await api.post(`/payments/${placed.paymentId}/pay`, card);               // 202: "accepted"; the REAL result arrives by webhook
      setPhase("processing");
      // Poll until the gateway's webhook has been processed (2 s normally, 30 s for ".99" amounts)
      for (let i = 0; i < 40; i++) {
        await new Promise((r) => setTimeout(r, 2000));
        const { data } = await api.get<PaymentStatus>(`/payments/${placed.paymentId}`);
        if (data.status === "Succeeded") return nav(`/orders/${placed.orderId}`);
        if (data.status === "Failed") { setPhase("declined"); setError(`Payment failed (${data.failureCode}). Your cart was restored — you can try again.`); return; }
      }
      setError("Still waiting for confirmation. Check 'My orders' in a moment.");
    } catch (e) { setError(toApiError(e).title); setPhase("form"); } finally { setBusy(false); }
  }

  if (cart.isLoading || addresses.isLoading) return <p>{t("common.loading")}</p>;
  // …render: address picker, order summary (lkr(cart.data.subtotalCents)), "Place order" button (disabled while busy),
  //    then once `placed` exists a card form (react-hook-form) with a hint: "Test card 4111 1111 1111 1111, any future date, CVC 123",
  //    a spinner for phase === "processing", and an <ErrorBanner message={error} />.
  return <div>{/* see comment above — build the form with Tailwind */}</div>;
}
```
🧠 *The three guarantees in this page:* **(1)** `idemKey` → no duplicate orders on retries; **(2)** polling → the UI reflects the **webhook-confirmed** truth, never the optimistic "202"; **(3)** on decline we show a clear message — the backend already restored stock and cart.

### 1.13.6 The rest of the pages — a build map

| Page / route | API calls | Notes |
|---|---|---|
| `/` Home | `GET /products`, `GET /categories` | search box + category chips + sort; pagination via `page` |
| `/p/:slug` Product | `GET /products/{slug}`, `GET /inventory/availability`, `GET /recommendations/related/{id}` | variant selector; disable "Add" when `out_of_stock`; related row from the Python service (§1.15) |
| `/cart` | `GET /cart`, `PATCH/DELETE /cart/items/*` | show unavailable lines greyed; "Checkout" → login if guest |
| `/register` | `POST /auth/register` → redirect `/verify?email=…` | role = `customer`; Zod rules mirror the API (min 10-char password) |
| `/verify` | `POST /auth/verify-otp` ×2, `POST /auth/resend-otp` | **dev helper**: a "fill code" button calling `GET /dev/otp/{dest}` (only rendered when `import.meta.env.DEV`) |
| `/login` | `POST /auth/login` → `AuthContext.login` | show `ACCOUNT_LOCKED` / `INVALID_CREDENTIALS` messages from `code` |
| `/account/addresses` | `GET/POST/PUT/DELETE /customers/me/addresses` | district `<select>` from the 25 districts |
| `/checkout` | see §1.13.5 | `RequireAuth` + requires `emailVerified && phoneVerified` else redirect `/verify` |
| `/orders`, `/orders/:id` | `GET /orders`, `/orders/{id}`, `/orders/{id}/tracking` | timeline from `tracking.timeline`; "Cancel" only while `PendingPayment` |

`src/auth/RequireAuth.tsx`:
```tsx
import { Navigate, Outlet, useLocation } from "react-router";
import { useAuth } from "./AuthContext";

export default function RequireAuth({ verified = false }: { verified?: boolean }) {
  const { me, loading } = useAuth();
  const loc = useLocation();
  if (loading) return null;
  if (!me) return <Navigate to="/login" state={{ from: loc.pathname }} replace />;
  if (verified && !(me.emailVerified && me.phoneVerified)) return <Navigate to="/verify" replace />;
  return <Outlet />;
}
```
`src/App.tsx` (routes):
```tsx
import { BrowserRouter, Route, Routes } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthProvider } from "./auth/AuthContext";
import RequireAuth from "./auth/RequireAuth";
// import pages …

const qc = new QueryClient({ defaultOptions: { queries: { staleTime: 15_000, retry: 1 } } });

export default function App() {
  return (
    <QueryClientProvider client={qc}><AuthProvider><BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<HomePage />} />
          <Route path="p/:slug" element={<ProductPage />} />
          <Route path="cart" element={<CartPage />} />
          <Route path="login" element={<LoginPage />} />
          <Route path="register" element={<RegisterPage />} />
          <Route path="verify" element={<VerifyPage />} />
          <Route element={<RequireAuth />}>
            <Route path="orders" element={<OrdersPage />} />
            <Route path="orders/:id" element={<OrderDetailPage />} />
            <Route path="account/addresses" element={<AddressesPage />} />
          </Route>
          <Route element={<RequireAuth verified />}><Route path="checkout" element={<CheckoutPage />} /></Route>
        </Route>
      </Routes>
    </BrowserRouter></AuthProvider></QueryClientProvider>
  );
}
```

### 1.13.7 i18n skeleton (so Phase 3 is just adding files)

`src/lib/i18n.ts`
```ts
import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import en from "../locales/en.json";

i18n.use(initReactI18next).init({
  resources: { en: { translation: en } },   // Phase 3: add si.json + ta.json, or load packs from the Localization service
  lng: "en", fallbackLng: ["en"],            // fallback chain si → ta → en arrives with the other languages
  interpolation: { escapeValue: false },     // React already escapes output
});
export default i18n;
```
`src/locales/en.json` — start with `{ "common": { "loading": "Loading…" }, "cart": { "title": "Your cart" }, "checkout": { "placeOrder": "Place order" }, … }`. **Rule:** every string the user can see comes from `t('...')`. Retrofitting this later means touching every file.

✅ **Check (end-to-end in the browser)**
1. `python scripts/dev.py` + `npm run dev` → `http://localhost:5173`.
2. Seed demo data first (§1.17.2: `python scripts/seed_demo.py`) so there's something to buy.
3. As a guest: search, open a product, add to cart, reload the page (cart persists via `X-Guest-Id`). Register → verify (dev helper) → login → the guest cart is **merged**.
4. Checkout with the test card → "processing" → redirected to the order page showing `Paid` and a timeline. Repeat with the declined card, and with a `.99` total.
5. Open DevTools → Network: confirm **no `price` field is ever sent** to the API; confirm the checkout POST carries `Idempotency-Key`.
6. Let the access token expire (set `AccessTokenMinutes` to 1 temporarily) → keep clicking → the site keeps working with **one** `/auth/refresh` call (check Network), not several.
7. `npm run build` succeeds and `npm run lint` is clean (CI runs both).

⚠️ **Pitfalls:** *CORS error* → you used an absolute URL (`http://localhost:5100/...`) instead of `/api/v1` through the proxy · *infinite re-render* → don't create objects/arrays inline in `queryKey` dependencies carelessly · *prices look 100× too big* → you forgot `cents / 100` (always use `lkr()`) · *refresh loop* → the refresh call itself must not go through the interceptor that retries on 401 (we use bare `axios` for it).

Commit: `feat(web): storefront with cart, checkout, orders`.

---

## 1.14 Flutter customer app (a thin vertical slice)

🎯 **Goal:** prove the **same API** serves mobile: *login → product list with search → product detail*. (Cart/checkout/orders are Phase 7 polish — you will copy the React logic you already understand.)

🧠 **Why only a slice now?** Every screen you build before the API is stable is a screen you rebuild. A slice validates the hard parts (networking, token storage, refresh, emulator quirks) with minimal code.

### 1.14.1 Setup

```bash
cd src/clients
flutter create customer_app --org lk.lankamart --platforms android,ios
cd customer_app
flutter pub add dio flutter_secure_storage flutter_riverpod go_router
flutter doctor            # must be green for the platform you test on
```

**The emulator networking trap (the #1 Flutter beginner problem):**

| Where the app runs | Gateway address |
|---|---|
| Android emulator | `http://10.0.2.2:5100` (`10.0.2.2` = "your computer" as seen from the emulator; `localhost` would mean the emulator itself) |
| iOS simulator | `http://localhost:5100` |
| Real phone on Wi-Fi | `http://<your-computer-LAN-IP>:5100` (and allow it through your firewall) |

Android blocks plain `http://` by default. For **debug builds only**, edit `android/app/src/debug/AndroidManifest.xml` and add `android:usesCleartextTraffic="true"` to the `<application>` tag. (Release builds will use HTTPS — Phase 6.)

### 1.14.2 API client with single-flight token refresh

`lib/core/api_client.dart`
```dart
import 'dart:async';
import 'dart:io' show Platform;
import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

String get baseUrl => Platform.isAndroid ? 'http://10.0.2.2:5100/api/v1' : 'http://localhost:5100/api/v1';

/// Tokens go in the platform keystore (Android Keystore / iOS Keychain) — NOT SharedPreferences (plain text).
class TokenStore {
  static const _s = FlutterSecureStorage();
  static Future<String?> get access => _s.read(key: 'access');
  static Future<String?> get refresh => _s.read(key: 'refresh');
  static Future<void> save(String a, String r) async { await _s.write(key: 'access', value: a); await _s.write(key: 'refresh', value: r); }
  static Future<void> clear() => _s.deleteAll();
}

class ApiClient {
  ApiClient._() {
    dio.interceptors.add(InterceptorsWrapper(
      onRequest: (o, h) async {
        final t = await TokenStore.access;
        if (t != null) o.headers['Authorization'] = 'Bearer $t';
        h.next(o);
      },
      onError: (e, h) async {
        final isAuthCall = RegExp(r'/auth/(login|register|refresh|verify-otp)').hasMatch(e.requestOptions.path);
        if (e.response?.statusCode == 401 && !isAuthCall && e.requestOptions.extra['retried'] != true) {
          // SINGLE-FLIGHT: many requests failing together share ONE refresh (parallel refreshes look like token theft to the server)
          _refreshing ??= _refresh().whenComplete(() => _refreshing = null);
          if (await _refreshing!) {
            final o = e.requestOptions..extra['retried'] = true;
            o.headers['Authorization'] = 'Bearer ${await TokenStore.access}';
            return h.resolve(await dio.fetch(o));
          }
        }
        h.next(e);
      },
    ));
  }
  static final ApiClient instance = ApiClient._();

  final Dio dio = Dio(BaseOptions(baseUrl: baseUrl, connectTimeout: const Duration(seconds: 10), receiveTimeout: const Duration(seconds: 15)));
  Future<bool>? _refreshing;

  Future<bool> _refresh() async {
    final rt = await TokenStore.refresh;
    if (rt == null) return false;
    try {
      // a bare Dio (no interceptors) so the refresh call can't trigger itself
      final r = await Dio(BaseOptions(baseUrl: baseUrl)).post('/auth/refresh', data: {'refreshToken': rt});
      await TokenStore.save(r.data['accessToken'], r.data['refreshToken']);
      return true;
    } catch (_) { await TokenStore.clear(); return false; }
  }

  Future<void> login(String email, String password) async {
    final r = await dio.post('/auth/login', data: {'email': email, 'password': password});
    await TokenStore.save(r.data['accessToken'], r.data['refreshToken']);
  }
}
```

### 1.14.3 Model, provider, screens

`lib/features/products/product.dart` and a provider:
```dart
class ProductItem {
  final String id, slug, name; final String? imageUrl; final int priceFromCents;
  ProductItem.fromJson(Map<String, dynamic> j)
      : id = j['id'], slug = j['slug'], name = j['name'], imageUrl = j['imageUrl'], priceFromCents = j['priceFromCents'];
}
String lkr(int cents) => 'LKR ${(cents / 100).toStringAsFixed(2)}';   // display only; money stays integer cents in logic

final searchProvider = StateProvider<String>((_) => '');
final productsProvider = FutureProvider.autoDispose<List<ProductItem>>((ref) async {
  final q = ref.watch(searchProvider);
  final r = await ApiClient.instance.dio.get('/products', queryParameters: {'query': q, 'page_size': 20});
  return (r.data['items'] as List).map((e) => ProductItem.fromJson(e)).toList();
});
```
`lib/main.dart` (wiring):
```dart
void main() => runApp(const ProviderScope(child: App()));

final router = GoRouter(routes: [
  GoRoute(path: '/', builder: (_, __) => const ProductListScreen()),
  GoRoute(path: '/login', builder: (_, __) => const LoginScreen()),
  GoRoute(path: '/p/:slug', builder: (_, s) => ProductDetailScreen(slug: s.pathParameters['slug']!)),
]);
class App extends StatelessWidget {
  const App({super.key});
  @override Widget build(BuildContext context) => MaterialApp.router(routerConfig: router, title: 'LankaMart');
}
```
**Screens to build** (each ~40 lines; the pattern is `ref.watch(provider).when(data:…, loading:…, error:…)`):
* `LoginScreen` — two `TextField`s, calls `ApiClient.instance.login`, shows the API's `title` on error (`DioException.response?.data['title']`), then `context.go('/')`.
* `ProductListScreen` — search field updating `searchProvider`, `ListView` of cards (image via `Image.network`, name, `lkr(priceFromCents)`), tap → `context.go('/p/${p.slug}')`.
* `ProductDetailScreen` — `GET /products/{slug}`, show images, variants and prices.

✅ **Check:** gateway + services running, `flutter run` on an emulator → log in with `customer1@lankamart.local` (created by the seeder, §1.17.2), see seeded products, search "shoe". Turn on airplane mode → you see a friendly error, not a crash. Set `AccessTokenMinutes` to 1, wait, pull to refresh → still works (one refresh call in the Identity log).

⚠️ **Pitfalls:** *connection refused* → wrong base URL for the platform (table above) · *cleartext not permitted* → the debug manifest line · `flutter_secure_storage` on Android needs `minSdkVersion` ≥ 23 (set in `android/app/build.gradle*` if the build complains).

Commit: `feat(mobile): flutter slice – login and browse`.

---

## 1.15 Python recommender service (your algorithms home)

**Port 5110 · FastAPI · no database.**
🎯 **Goal:** the first real piece of your **recommendation algorithm** (System Scenarios §2.2): *related products* now, plus the **behaviour-scoring functions** (tested, pure, waiting for Phase 5's event stream).

🧠 **Design**
* It obeys **Principle #1**: it never touches another service's DB — it reads the Catalog's **public** API (through the gateway) and builds an in-memory index every 5 minutes.
* The algorithm is **pure Python functions** (`related.py`, `scoring.py`) separate from the web layer (`main.py`). Pure functions are testable without servers — that's how you iterate on an algorithm quickly and safely.
* **Related-products score** = `0.50 × name-similarity + 0.35 × same-category + 0.15 × price-closeness`. Name similarity uses **TF-IDF over character n-grams** — which also works for **Sinhala and Tamil and tolerates typos**, with no language-specific tooling.
* **Behaviour scoring** (Tier 2, from the scenarios): `product_view +1 · search +2 · add_to_cart +4 · wishlist +5 · purchase +10 · removed_from_cart −3 · dwell +1 per 30 s (capped)`, with **time decay** (a signal loses half its weight every 14 days) so old interests fade.

### 1.15.1 Setup

```bash
cd src/ml/recommender
python -m venv .venv
source .venv/bin/activate          # Windows PowerShell: .venv\Scripts\Activate.ps1
pip install fastapi "uvicorn[standard]" httpx scikit-learn numpy pytest
pip freeze > requirements.txt      # pins exact versions for CI/Docker
mkdir app tests && touch app/__init__.py
```

`app/related.py`
```python
from __future__ import annotations
from dataclasses import dataclass

from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.metrics.pairwise import cosine_similarity

W_TEXT, W_CATEGORY, W_PRICE = 0.50, 0.35, 0.15


@dataclass(frozen=True)
class Item:
    id: str
    slug: str
    name: str
    category_id: str
    price_cents: int
    image_url: str | None = None


class RelatedIndex:
    """Immutable snapshot of the catalogue. Rebuilt periodically; never mutated while serving."""

    def __init__(self, items: list[Item]):
        self.items = items
        self.pos = {it.id: i for i, it in enumerate(items)}
        self._matrix = None
        if items:
            # char n-grams: robust to typos and to scripts without spaces/stemmers (Sinhala, Tamil)
            self._vec = TfidfVectorizer(analyzer="char_wb", ngram_range=(3, 5), lowercase=True)
            self._matrix = self._vec.fit_transform([it.name for it in items])

    def related(self, product_id: str, k: int = 8) -> list[tuple[Item, float]]:
        i = self.pos.get(product_id)
        if i is None or self._matrix is None:
            return []
        base = self.items[i]
        text_sim = cosine_similarity(self._matrix[i], self._matrix).ravel()
        scored: list[tuple[Item, float]] = []
        for j, it in enumerate(self.items):
            if j == i:
                continue                                           # never recommend the product to itself
            same_cat = 1.0 if it.category_id == base.category_id else 0.0
            lo, hi = sorted((it.price_cents, base.price_cents))
            price_sim = lo / hi if hi else 0.0                     # 1.0 = same price, → 0 as they diverge
            scored.append((it, W_TEXT * float(text_sim[j]) + W_CATEGORY * same_cat + W_PRICE * price_sim))
        scored.sort(key=lambda t: t[1], reverse=True)
        return scored[:k]
```

`app/scoring.py`
```python
from __future__ import annotations
from collections import defaultdict
from dataclasses import dataclass
from datetime import datetime

WEIGHTS = {"product_view": 1.0, "search_query": 2.0, "add_to_cart": 4.0, "wishlist_add": 5.0, "purchase": 10.0, "removed_from_cart": -3.0}
DWELL_POINTS_PER_30S, DWELL_CAP = 1.0, 3.0
HALF_LIFE_DAYS = 14.0


@dataclass(frozen=True)
class Event:
    type: str
    at: datetime                       # timezone-aware UTC
    category_id: str | None = None
    dwell_seconds: float = 0.0


def decay(age_days: float) -> float:
    return 0.5 ** (age_days / HALF_LIFE_DAYS)


def event_score(e: Event, now: datetime) -> float:
    base = WEIGHTS.get(e.type, 0.0)
    if e.type == "product_view" and e.dwell_seconds > 30:
        base += min(DWELL_CAP, (e.dwell_seconds // 30) * DWELL_POINTS_PER_30S)
    age_days = max((now - e.at).total_seconds() / 86400.0, 0.0)
    return base * decay(age_days)


def category_affinity(events: list[Event], now: datetime) -> dict[str, float]:
    """Share of the user's (decayed, positive) interest per category; sums to 1.0. Used to re-rank candidates."""
    totals: dict[str, float] = defaultdict(float)
    for e in events:
        if e.category_id:
            totals[e.category_id] += event_score(e, now)
    positive = {k: v for k, v in totals.items() if v > 0}
    s = sum(positive.values())
    return {k: v / s for k, v in positive.items()} if s else {}
```

`app/main.py`
```python
import asyncio
import os
from contextlib import asynccontextmanager

import httpx
from fastapi import FastAPI, HTTPException, Query

from .related import Item, RelatedIndex

CATALOG_URL = os.getenv("CATALOG_URL", "http://localhost:5100/api/v1")   # the PUBLIC API via the gateway — no internal key, no DB
REFRESH_SECONDS = int(os.getenv("REFRESH_SECONDS", "300"))
state: dict[str, RelatedIndex] = {"index": RelatedIndex([])}


async def load_items() -> list[Item]:
    items: list[Item] = []
    async with httpx.AsyncClient(base_url=CATALOG_URL, timeout=10) as c:
        for cat in (await c.get("/categories")).json():
            page = 1
            while True:
                r = (await c.get("/products", params={"category": cat["id"], "page": page, "page_size": 50})).json()
                items += [Item(p["id"], p["slug"], p["name"], cat["id"], p["priceFromCents"], p.get("imageUrl")) for p in r["items"]]
                if page >= r["totalPages"]:
                    break
                page += 1
    return items


async def refresher():
    while True:
        try:
            state["index"] = RelatedIndex(await load_items())          # build the NEW index fully, then swap the pointer (no half-built reads)
        except Exception as e:                                         # catalogue down? keep serving the old index
            print("index refresh failed:", e)
        await asyncio.sleep(REFRESH_SECONDS)


@asynccontextmanager
async def lifespan(_: FastAPI):
    task = asyncio.create_task(refresher())
    yield
    task.cancel()


app = FastAPI(title="LankaMart Recommender", lifespan=lifespan)


@app.get("/health")
def health():
    return {"status": "ok", "items": len(state["index"].items)}


@app.get("/api/v1/recommendations/related/{product_id}")
def related(product_id: str, limit: int = Query(8, ge=1, le=20)):
    idx = state["index"]
    if product_id not in idx.pos:
        raise HTTPException(404, "unknown product")
    return [{"id": it.id, "slug": it.slug, "name": it.name, "imageUrl": it.image_url, "priceFromCents": it.price_cents, "score": round(s, 4)}
            for it, s in idx.related(product_id, limit)]
```

`tests/test_related.py` and `tests/test_scoring.py`
```python
# tests/test_related.py
from app.related import Item, RelatedIndex

ITEMS = [
    Item("1", "nike-air-zoom-running-shoes", "Nike Air Zoom Running Shoes", "fashion", 1_500_000),
    Item("2", "adidas-ultraboost-running-shoes", "Adidas Ultraboost Running Shoes", "fashion", 1_650_000),
    Item("3", "stainless-steel-water-bottle", "Stainless Steel Water Bottle", "home", 250_000),
    Item("4", "samsung-galaxy-phone", "Samsung Galaxy Phone", "electronics", 9_000_000),
]

def test_similar_product_ranks_first():
    top = RelatedIndex(ITEMS).related("1", k=3)
    assert top[0][0].id == "2"

def test_never_recommends_itself():
    assert all(it.id != "1" for it, _ in RelatedIndex(ITEMS).related("1", k=10))

def test_unknown_product_and_empty_catalogue():
    assert RelatedIndex(ITEMS).related("nope") == []
    assert RelatedIndex([]).related("1") == []

# tests/test_scoring.py
from datetime import datetime, timedelta, timezone
from app.scoring import Event, category_affinity, event_score

NOW = datetime(2026, 11, 9, tzinfo=timezone.utc)

def test_purchase_beats_view():
    assert event_score(Event("purchase", NOW), NOW) > event_score(Event("product_view", NOW), NOW)

def test_signal_halves_after_14_days():
    old = Event("purchase", NOW - timedelta(days=14))
    assert abs(event_score(old, NOW) - 5.0) < 1e-9

def test_removed_from_cart_is_negative():
    assert event_score(Event("removed_from_cart", NOW), NOW) < 0

def test_dwell_bonus_is_capped():
    long_view = Event("product_view", NOW, dwell_seconds=3600)
    assert event_score(long_view, NOW) == 1.0 + 3.0

def test_affinity_sums_to_one():
    aff = category_affinity([Event("purchase", NOW, "fashion"), Event("add_to_cart", NOW, "electronics")], NOW)
    assert abs(sum(aff.values()) - 1.0) < 1e-9 and aff["fashion"] > aff["electronics"]
```

Run: `pytest -q` (tests) and `uvicorn app.main:app --port 5110 --reload` (service).

`Dockerfile` (same folder):
```dockerfile
FROM python:3.12-slim
WORKDIR /srv
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY app ./app
RUN useradd -r -u 10001 appuser
USER appuser
EXPOSE 8080
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8080"]
```

✅ **Check:** tests pass; with seeded catalogue running, `curl http://localhost:5110/health` shows `items > 0`; `curl http://localhost:5100/api/v1/recommendations/related/<productId>` (through the gateway) returns similar products. In React's product page, render a "You may also like" row from this endpoint (if the service is down, **hide the row** — a recommender outage must never break the product page).

⚠️ **Pitfalls:** `scikit-learn` install is slow the first time · returning a 500 when the index is empty — we return an empty list/404 instead · **don't over-engineer the maths now**: the architecture doc's rule is *"start with related products by category; proper ML only when you have transaction volume to justify it."*

**What comes in later phases:** Phase 5 feeds `Event`s from the analytics stream (`product.viewed`, `cart.added`, `order.paid`) into `category_affinity`, stores per-user candidate sets in Redis, and re-ranks (affinity × price-band × seller rating × in-stock) — you already have the tested scoring core.

Commit: `feat(ml): related-products service and behaviour scoring core`.

---

## 1.16 Testing

🎯 **Goal:** a test suite that lets you change code without fear, and proves the dangerous paths: **money, stock, saga compensation, authorization, idempotency**.

🧠 **The pyramid (architecture doc §8), at Phase 1 scale:** many fast **unit tests** (pure logic) → fewer **integration tests** (real Postgres/Redis in throw-away containers via **Testcontainers** — *not* mocks, because mocks lie about SQL and locking) → a couple of **end-to-end** checks. **TDD tip:** for state machines and sagas, write the failing test first; it forces you to define the behaviour before coding it.

### 1.16.1 Projects

```bash
dotnet new xunit -n LankaMart.Orders.Tests    -o tests/LankaMart.Orders.Tests
dotnet new xunit -n LankaMart.Inventory.Tests -o tests/LankaMart.Inventory.Tests
dotnet new xunit -n LankaMart.Payments.Tests  -o tests/LankaMart.Payments.Tests
dotnet new xunit -n LankaMart.Catalog.Tests   -o tests/LankaMart.Catalog.Tests
dotnet new xunit -n LankaMart.Identity.Tests  -o tests/LankaMart.Identity.Tests
dotnet new xunit -n LankaMart.Users.Tests     -o tests/LankaMart.Users.Tests
# in each: reference its service project + BuildingBlocks + Contracts, then add:
dotnet add <testproj> package Testcontainers.PostgreSql
dotnet add <testproj> package Testcontainers.Redis                    # Orders + Inventory tests
dotnet add <testproj> package Microsoft.AspNetCore.Mvc.Testing --version "10.*"
dotnet sln add tests/*/*.csproj
```
*(The `xunit` template gives xUnit v2 where `IAsyncLifetime` methods return `Task`. If you pick the `xunit3` template, they return `ValueTask`.)* Testcontainers needs Docker running — **CI's `ubuntu-latest` runner already has Docker**, so the same tests run there unchanged.

### 1.16.2 Unit tests — pure logic (write these first)

```csharp
// tests/LankaMart.Orders.Tests/OrderStateMachineTests.cs
using LankaMart.Orders.Api.Domain;
using Xunit;

public class OrderStateMachineTests
{
    [Theory]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Paid)]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Expired)]
    [InlineData(OrderStatus.Paid, OrderStatus.Processing)]
    [InlineData(OrderStatus.OutForDelivery, OrderStatus.Delivered)]
    public void Allows_valid_transitions(OrderStatus from, OrderStatus to) => Assert.True(OrderStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Delivered)]     // can't skip steps
    [InlineData(OrderStatus.Cancelled, OrderStatus.Paid)]               // terminal states stay terminal
    [InlineData(OrderStatus.Expired, OrderStatus.Paid)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Processing)]         // no going backwards
    public void Rejects_invalid_transitions(OrderStatus from, OrderStatus to)
    {
        Assert.False(OrderStateMachine.CanTransition(from, to));
        Assert.Throws<BusinessRuleException>(() => OrderStateMachine.EnsureAllowed(from, to));
    }

    [Fact] public void Every_non_terminal_status_has_a_way_out() =>
        Assert.All(Enum.GetValues<OrderStatus>().Where(s => s is not (OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.PaymentFailed or OrderStatus.Expired or OrderStatus.Refunded)),
                   s => Assert.True(Enum.GetValues<OrderStatus>().Any(t => OrderStateMachine.CanTransition(s, t)), $"{s} is a dead end"));
}

// tests/LankaMart.Payments.Tests/MockRulesTests.cs
using LankaMart.Payments.Api.Gateway;
using Xunit;
public class MockRulesTests
{
    [Fact] public void Test_card_passes_luhn() => Assert.True(MockRules.Luhn("4111 1111 1111 1111"));
    [Fact] public void Typo_fails_luhn() => Assert.False(MockRules.Luhn("4111 1111 1111 1112"));
    [Theory]
    [InlineData("4111111111111111", 100000, MockOutcome.Success)]
    [InlineData("4000000000000002", 100000, MockOutcome.Declined)]
    [InlineData("4000000000000119", 100000, MockOutcome.GatewayError)]
    [InlineData("4111111111111111", 149999, MockOutcome.PendingThenSuccess)]   // .99 → pending
    [InlineData("4000000000000002", 149999, MockOutcome.Declined)]            // decline wins over pending
    public void Decides_outcome(string card, long cents, MockOutcome expected) => Assert.Equal(expected, MockRules.Decide(card, cents));
    [Fact] public void Signature_roundtrip_and_tamper_detection()
    {
        var sig = MockSigner.Sign("{\"a\":1}", "secret");
        Assert.True(MockSigner.Verify("{\"a\":1}", sig, "secret"));
        Assert.False(MockSigner.Verify("{\"a\":2}", sig, "secret"));        // body changed
        Assert.False(MockSigner.Verify("{\"a\":1}", sig, "other-secret"));  // wrong key
    }
}
```
Also unit-test: `SellerProfile` transitions (every allowed and forbidden step), `Slugs.From` (Unicode, punctuation, empty), `Money`/`PhoneNumber` (done in Phase 0), `OtpService` hashing helpers.

### 1.16.3 Integration test fixtures

`tests/LankaMart.Orders.Tests/Infra.cs`
```csharp
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

public sealed class InfraFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
    public string Pg => _pg.GetConnectionString();
    public string Redis => _redis.GetConnectionString();
    public async Task InitializeAsync() { await Task.WhenAll(_pg.StartAsync(), _redis.StartAsync()); }
    public async Task DisposeAsync() { await _pg.DisposeAsync(); await _redis.DisposeAsync(); }
}
[CollectionDefinition("infra")] public class InfraCollection : ICollectionFixture<InfraFixture> { }   // containers start ONCE for the whole test class group
```

### 1.16.4 Saga test — the failure path that matters most

Goal: *"when Inventory says out of stock, the order ends `Cancelled`, no payment is created, and `OrderCancelled` is published."* MassTransit's **test harness** is an in-memory bus that records what was published — no RabbitMQ needed.

```csharp
using LankaMart.Orders.Api.Clients;
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using LankaMart.Orders.Api.Features;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

// Hand-written fakes: tiny, explicit, no mocking library needed
file sealed class FakeCatalog(Guid variantId, Guid sellerId) : ICatalogClient
{
    public Task<IReadOnlyList<VariantInfo>> LookupAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VariantInfo>>([new VariantInfo(variantId, Guid.NewGuid(), sellerId, "SKU-1", "Test Shoe", null, 1_000_000, true)]);
}
file sealed class FakeInventory(bool success) : IInventoryClient
{
    public Task<ReserveStockResponse> ReserveAsync(ReserveStockRequest r, CancellationToken ct) => Task.FromResult(new ReserveStockResponse(success, success ? [] : r.Lines.Select(l => l.VariantId).ToList()));
}
file sealed class FakePayments(bool works) : IPaymentsClient
{
    public int Calls; 
    public Task<CreatePaymentIntentResponse> CreateIntentAsync(CreatePaymentIntentRequest r, CancellationToken ct)
    { Calls++; return works ? Task.FromResult(new CreatePaymentIntentResponse(Guid.NewGuid(), "mock")) : throw new HttpRequestException("down"); }
}

[Collection("infra")]
public class CheckoutSagaTests(InfraFixture infra)
{
    private async Task<(ServiceProvider sp, ITestHarness harness, FakePayments pay)> BuildAsync(bool stockOk, bool paymentsOk, Guid variantId, Guid userId)
    {
        var pay = new FakePayments(paymentsOk);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<OrdersDb>(o => o.UseNpgsql(infra.Pg).UseSnakeCaseNamingConvention());
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(infra.Redis));
        services.Configure<OrdersOptions>(_ => { });
        services.AddSingleton<ICatalogClient>(new FakeCatalog(variantId, Guid.NewGuid()));
        services.AddSingleton<IInventoryClient>(new FakeInventory(stockOk));
        services.AddSingleton<IPaymentsClient>(pay);
        services.AddScoped<CartService>(); services.AddScoped<CheckoutService>();
        services.AddMassTransitTestHarness();
        var sp = services.BuildServiceProvider(true);
        await using (var scope = sp.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<OrdersDb>().Database.MigrateAsync();
        var harness = sp.GetRequiredService<ITestHarness>(); await harness.Start();
        await sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase().HashSetAsync(CartService.UserKey(userId), variantId.ToString(), 2);   // cart with 2 items
        return (sp, harness, pay);
    }

    private static PlaceOrderRequest Req() => new(new AddressInput("Nimal Perera", "0771234567", "12 Galle Road", null, "Colombo", "Colombo", "00300"));

    [Fact]
    public async Task Out_of_stock_cancels_order_and_never_calls_payments()
    {
        Guid v = Guid.NewGuid(), u = Guid.NewGuid();
        var (sp, harness, pay) = await BuildAsync(stockOk: false, paymentsOk: true, v, u);
        await using var scope = sp.CreateAsyncScope();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<CheckoutService>().PlaceOrderAsync(u, Req(), Guid.NewGuid().ToString("N"), default));

        Assert.Equal("OUT_OF_STOCK", ex.Code);
        Assert.Equal(0, pay.Calls);                                                                              // never charged
        var order = await scope.ServiceProvider.GetRequiredService<OrdersDb>().Orders.AsNoTracking().SingleAsync(o => o.CustomerId == u);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.True(await harness.Published.Any<OrderCancelled>());                                              // compensation event emitted
    }

    [Fact]
    public async Task Payment_service_down_compensates_by_cancelling_and_releasing_stock()
    {
        Guid v = Guid.NewGuid(), u = Guid.NewGuid();
        var (sp, harness, _) = await BuildAsync(stockOk: true, paymentsOk: false, v, u);
        await using var scope = sp.CreateAsyncScope();

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => scope.ServiceProvider.GetRequiredService<CheckoutService>().PlaceOrderAsync(u, Req(), Guid.NewGuid().ToString("N"), default));

        Assert.Equal(OrderStatus.Cancelled, (await scope.ServiceProvider.GetRequiredService<OrdersDb>().Orders.AsNoTracking().SingleAsync(o => o.CustomerId == u)).Status);
        Assert.True(await harness.Published.Any<OrderCancelled>());                                              // → Inventory will release the reservation
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_same_order()
    {
        Guid v = Guid.NewGuid(), u = Guid.NewGuid();
        var (sp, _, _) = await BuildAsync(true, true, v, u);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<CheckoutService>(); var key = Guid.NewGuid().ToString("N");

        var first = await svc.PlaceOrderAsync(u, Req(), key, default);
        var second = await svc.PlaceOrderAsync(u, Req(), key, default);

        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<OrdersDb>().Orders.CountAsync(o => o.CustomerId == u));   // ONE order
    }
}
```
*(`file sealed class` = a class visible only inside this file. If your compiler dislikes the generic `ServiceProvider` plumbing, move the builder into a helper class — the lesson is the three assertions.)*

### 1.16.5 The flash-sale race test (Inventory)

```csharp
[Collection("infra")]
public class ReservationRaceTests(InfraFixture infra)
{
    [Fact]
    public async Task Two_hundred_parallel_buyers_for_fifty_units_exactly_fifty_win()
    {
        // arrange: InventoryDb on infra.Pg (migrate), one InventoryItem{QtyOnHand=50}, RedisStock on infra.Redis, ReservationService with a harness bus
        // act:     200 × Task.Run(() => svc.ReserveAsync(new ReserveStockRequest(Guid.NewGuid(), [new StockLine(variantId, 1)], 600), ct)) — each with its OWN scope/DbContext
        // assert:  results.Count(r => r.Success) == 50 ; Redis stock:{id} == 0 ; db QtyReserved == 50 ; QtyOnHand == 50 ; reservations table has 50 rows
        //          then release them all → Redis == 50 and QtyReserved == 0   (compensation restores everything)
        // ⚠ each parallel task needs its own DbContext (DbContext is NOT thread-safe) → create a scope per task
    }
}
```
Fill this in yourself — it is the most instructive test in Phase 1. *If it ever fails with 51 winners, you have found a real race condition.*

### 1.16.6 Authorization tests (IDOR, roles) with a fake auth scheme

`WebApplicationFactory` boots your real service in memory. Replace JWT with a test handler that reads headers, so tests can "be" any user:

```csharp
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, System.Text.Encodings.Web.UrlEncoder e)
    : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new("sub", user!), new("email_verified", "true"), new("phone_verified", "true") };
        foreach (var r in Request.Headers["X-Test-Roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)) claims.Add(new("role", r));
        if (Request.Headers.TryGetValue("X-Test-Seller", out var s)) claims.Add(new("seller_id", s!));
        var id = new ClaimsIdentity(claims, "Test", "sub", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), "Test")));
    }
}
// In the factory:  builder.ConfigureTestServices(s => s.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { })
//                                                    .Services.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = "Test"));
```
Then every IDOR rule becomes one readable test:
```csharp
[Fact] public async Task Customer_cannot_read_someone_elses_order()
{
    var order = await SeedOrderFor(customerId: A);
    var res = await Client(user: B, roles: "customer").GetAsync($"/api/v1/orders/{order.Id}");
    Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);                  // 404, NOT 403 (don't confirm existence)
}
```

### 1.16.7 The mandatory test list (your Definition-of-Done for tests)

| Service | Must be covered (✔ = write it) |
|---|---|
| **Identity** | ✔ register ok / duplicate email / duplicate phone / weak password / `role:"super_admin"` rejected · ✔ OTP ok / wrong / 6th attempt locked / expired · ✔ login ok / bad password / lockout after 5 · ✔ refresh rotates · ✔ **reuse of an old refresh token revokes the family** · ✔ tampered token & `alg:none` rejected |
| **Users** | ✔ consumer creates profile once even if delivered twice · ✔ seller state machine (all allowed + forbidden transitions) · ✔ KYC blocked until verified · ✔ customer manager gets 403 on seller admin · ✔ **address IDOR → 404** |
| **Catalog** | ✔ unapproved seller → 403 `SELLER_NOT_APPROVED` · ✔ seller B cannot edit seller A's product (404) · ✔ search escapes `%`/`_` · ✔ `page_size` capped · ✔ suspended seller's products hidden · ✔ price never leaks cost fields · ✔ stale `SellerApproved` event ignored |
| **Inventory** | ✔ reserve idempotent · ✔ **200 buyers / 50 units** · ✔ release restores both Redis and Postgres · ✔ confirm idempotent · ✔ sweeper releases expired · ✔ adjust: other seller's variant → 404, would-go-negative → 422 |
| **Orders** | ✔ state machine · ✔ checkout: success / out-of-stock / payment-down (compensation) / **idempotency** / empty cart / unverified user · ✔ payment-completed consumer twice = one `OrderPaid` · ✔ payment-failed restores cart · ✔ expiry worker · ✔ **IDOR on orders** · ✔ guest cart merge takes max qty |
| **Payments** | ✔ mock rules · ✔ signature verify · ✔ webhook: duplicate (idempotent), bad signature (401), amount mismatch (422), unknown payment · ✔ ledger entries balanced (property: for random amounts `SUM(debit)=SUM(credit)`) · ✔ pay twice → 409 · ✔ other user's payment → 404 |
| **Gateway** | ✔ `/api/v1/internal/*` → 404 · ✔ protected route without token → 401 · ✔ blacklisted `jti` → 401 · ✔ rate limit → 429 |
| **Python** | ✔ `pytest` green (done in §1.15) |
| **React** | ✔ `npm run build` + `lint` in CI · ✔ one **Playwright** happy-path test (register → buy → paid) is a great Phase 2 addition |

✅ **Check:** `dotnet test` green locally **and** on GitHub Actions; add a *coverage target* only after the list above is done (coverage % is a poor goal; *dangerous paths covered* is the goal).

⚠️ **Pitfalls:** tests that pass only when run in a certain order (each test must create its own data with fresh GUIDs, as above) · sharing one `DbContext` across parallel tasks · using the EF *InMemory* provider (it does not enforce unique indexes, check constraints, or `ExecuteUpdate` semantics — your tests would pass while production fails) · flaky time-based tests — inject `TimeProvider` (we did) and use a fake clock when you need to "wait 15 minutes".

Commit: `test: unit + integration tests for saga, inventory race, payments, authorization`.

---

## 1.17 Week-by-week plan, demo-data seeder, and the cut list

### 1.17.1 The plan (Weeks 3–10 of the roadmap)

Assume **15–20 h/week**. If a week slips, slip the *calendar*, never the *Definition of Done* — every week ends with something that runs.

| Week | Build | Deliverable you can demonstrate | Hours (approx.) |
|---|---|---|---|
| **3** | §1.5 Contracts · **§1.6 Identity** · §1.7 Gateway hardening | Register → OTP → login → `/me` through the gateway; refresh rotation + reuse detection; rate limiting; logout blacklist | 18 |
| **4** | **§1.8 Users** (+ first event consumer) | Profile auto-created from `UserRegistered`; addresses with IDOR protection; seller KYC → manager approval | 18 |
| **5** | **§1.9 Catalog** | Approved seller creates & publishes a product with variants; search with `ILIKE`; seller gate + ownership tests | 18 |
| **6** | **§1.10 Inventory** + §1.17.2 seeder | Redis Lua reservations; sweeper; **200-buyers race test passes**; demo data seeded | 18 |
| **7** | **§1.11 Orders** part 1: cart, state machine, checkout up to *reserve* | Guest cart + merge; order created; compensation works when Payments is down | 18 |
| **8** | **§1.12 Payments** + finish the saga | Full backend happy path **and** all 10 failure checks; ledger balanced; **tag `phase-1-backend`** | 20 |
| **9** | **§1.13 React storefront** | Browse → cart → register/verify → checkout → paid, in the browser | 20 |
| **10** | §1.14 Flutter slice · §1.15 Python · §1.16 test gaps · §1.18 security pass · §1.19 Docker apps · §1.20 gate | Everything in containers; demo script rehearsed; tag `phase-1-complete` | 20 |

**Weekly rhythm (works well for beginners):** Mon–Tue: read the section + write the *tests* or the domain model · Wed–Thu: endpoints/consumers · Fri: run every ✅ check, fix, commit, push · weekend (optional): read about what confused you. **Friday ritual:** write 5 lines in `docs/journal.md` — *what I built, what broke, what I learned, what I'll do Monday, one question.* This is how you retain it.

### 1.17.2 Demo-data seeder (`scripts/seed_demo.py`)

🧠 *Why a script that uses the real API instead of SQL inserts?* It exercises the **whole system** (registration, events, approvals, catalog, inventory) exactly like a user would — if the seeder works, the platform works. It also doubles as a smoke test.

```bash
pip install requests
python scripts/seed_demo.py          # needs: docker compose up -d  +  python scripts/dev.py  (Development mode, for the /dev/otp endpoint)
```

```python
#!/usr/bin/env python3
"""Creates: 1 approved seller (+ ~25 published products with stock), 1 verified customer with an address.  Safe to re-run."""
import sys, time, urllib.parse
import requests

BASE = "http://localhost:5100/api/v1"
PASSWORD = "Sup3rSecret!!"
S = requests.Session()


def call(method, path, token=None, **kw):
    headers = kw.pop("headers", {})
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return S.request(method, BASE + path, headers=headers, timeout=20, **kw)


def dev_otp(destination):
    return call("GET", "/dev/otp/" + urllib.parse.quote(destination, safe="")).json()["code"]


def ensure_user(email, phone, name, role):
    r = call("POST", "/auth/register", json=dict(email=email, phone=phone, password=PASSWORD, fullName=name, role=role))
    if r.status_code == 202:
        e164 = "+94" + phone[1:]
        for channel, dest in (("email", email), ("phone", e164)):
            code = dev_otp(dest)
            call("POST", "/auth/verify-otp", json=dict(email=email, channel=channel, code=code)).raise_for_status()
        print(f"  registered + verified {email}")
    elif r.status_code != 409:
        sys.exit(f"register failed: {r.status_code} {r.text}")
    return login(email, PASSWORD)


def login(email, password):
    r = call("POST", "/auth/login", json=dict(email=email, password=password))
    r.raise_for_status()
    return r.json()["accessToken"]


def main():
    print("Seller …")
    seller = ensure_user("seller1@lankamart.local", "0771000001", "Kamal Fernando", "seller")
    me = call("GET", "/auth/me", seller).json()
    for _ in range(30):                                   # profile is created asynchronously by the UserRegistered event
        if call("GET", "/sellers/me", seller).status_code == 200:
            break
        time.sleep(1)
    st = call("GET", "/sellers/me", seller).json()["status"]
    if st in ("Registered", "Rejected"):
        kyc = dict(businessName="Fernando Traders", businessType="registered", brNumber="PV 12345", goodsCategory="General",
                   nicNumber="901234567V", addressLine="45 Main Street", district="Colombo",
                   bankName="Commercial Bank", bankBranch="Colombo 03", bankAccountName="Fernando Traders", bankAccountNumber="8001234567")
        call("POST", "/sellers/kyc", seller, json=kyc).raise_for_status()
        print("  KYC submitted")
    if st != "Active":
        admin = login("sellermgr@lankamart.local", "Manager#12345678")
        call("POST", f"/admin/sellers/{me['userId']}/approve", admin).raise_for_status()
        print("  seller approved by the Seller Manager")

    cats = {c["slug"]: c["id"] for c in call("GET", "/categories").json()}
    # category slug → [(name, brand, price in LKR)]
    catalogue = {
        "electronics": [("Galaxy A15 Smartphone", "Samsung", 62999), ("Wireless Earbuds Pro", "SoundCore", 8999), ("20000mAh Power Bank", "Anker", 6499)],
        "fashion": [("Running Shoes Air Zoom", "Nike", 18500), ("Classic Cotton T-Shirt", "Odel", 1850), ("Slim Fit Jeans", "Cotton Collection", 5400)],
        "home-living": [("Stainless Steel Water Bottle 1L", "Hydro", 2400), ("Rice Cooker 1.8L", "Singer", 11900), ("Cotton Bed Sheet Set", "Linea", 7800)],
        "beauty-health": [("Herbal Face Wash", "Ayur", 950), ("Vitamin C Serum", "Glow", 3200), ("Electric Toothbrush", "Oral-B", 9800)],
        "groceries": [("Ceylon Black Tea 400g", "Mlesna", 1250), ("Basmati Rice 5kg", "Araliya", 2650), ("Coconut Oil 1L", "Marina", 1450)],
        "sports-outdoors": [("Yoga Mat 6mm", "Decathlon", 3900), ("Badminton Racket Set", "Yonex", 7500), ("Cricket Bat English Willow", "Gunn", 23500)],
        "books-stationery": [("A5 Hardcover Notebook", "Atlas", 650), ("Gel Pen Pack of 10", "Pilot", 890), ("Scientific Calculator", "Casio", 4300)],
        "toys-baby": [("Wooden Building Blocks 100pc", "Playtime", 3600), ("Baby Stroller Lite", "Chicco", 28900), ("Remote Control Car", "Hot Wheels", 4700)],
    }
    catalogue["fashion"].append(("DEMO Pending-Payment Test Item (total ends .99)", "Demo", 1149.99))   # 114,999 + 35,000 delivery = 149,999 → ends .99 → pending webhook

    n = 0
    existing = {p["name"] for p in call("GET", "/seller/products", seller).json()} if call("GET", "/seller/products", seller).status_code == 200 else set()
    for slug, items in catalogue.items():
        for name, brand, price in items:
            if name in existing:
                continue
            cents = round(price * 100)
            variants = [dict(sku=f"{slug[:3].upper()}-{n:03d}-A", attributes={"edition": "Standard"}, priceCents=cents)]
            if slug == "fashion" and "DEMO" not in name:
                variants = [dict(sku=f"FSH-{n:03d}-{s}", attributes={"size": s}, priceCents=cents) for s in ("S", "M", "L")]
            if slug == "electronics":
                variants.append(dict(sku=f"ELE-{n:03d}-B", attributes={"edition": "Plus"}, priceCents=round(cents * 1.15)))
            body = dict(categoryId=cats[slug], brand=brand,
                        translations=[dict(lang="en", name=name, description=f"{name} by {brand}. Genuine product with warranty.")],
                        variants=variants, images=[dict(url=f"https://picsum.photos/seed/{n}/600/600", alt=name)])
            r = call("POST", "/seller/products", seller, json=body)
            if r.status_code == 403:                      # approval event hasn't reached Catalog yet — wait and retry once
                time.sleep(3); r = call("POST", "/seller/products", seller, json=body)
            r.raise_for_status()
            call("POST", f"/seller/products/{r.json()['id']}/publish", seller).raise_for_status()
            n += 1
    print(f"  published {n} new products (each variant starts with demo stock in Development)")

    print("Customer …")
    cust = ensure_user("customer1@lankamart.local", "0771000002", "Nimal Perera", "customer")
    for _ in range(30):
        if call("GET", "/customers/me", cust).status_code == 200:
            break
        time.sleep(1)
    if not call("GET", "/customers/me/addresses", cust).json():
        call("POST", "/customers/me/addresses", cust, json=dict(label="Home", recipientName="Nimal Perera", phone="0771000002", line1="12 Galle Road",
                                                                 city="Colombo", district="Colombo", postalCode="00300", isDefault=True)).raise_for_status()
    print("\nDone.  Customer: customer1@lankamart.local / " + PASSWORD + "   Seller: seller1@lankamart.local / " + PASSWORD)


if __name__ == "__main__":
    main()
```
*(Prices are in LKR in the script and converted to cents; `1149.99` → `114999` cents.)*

✅ **Check:** run it twice — the second run creates nothing new and doesn't crash (idempotent). Open the React site: products visible. `GET /api/v1/inventory/availability?variantIds=…` says `in_stock`.

### 1.17.3 The cut list — what to drop if you fall behind

Drop **in this order**, never from the top of the list downward in importance:
1. Flutter slice (§1.14) → *do it in Phase 2*.
2. Python recommender (§1.15) → keep `scoring.py` + tests only (they're pure code, 1 hour).
3. Scalar/OpenAPI polish, Jaeger tracing (nice, not required).
4. Address-book CRUD polish (keep add + list).
5. React styling (functional > pretty).

**Never cut:** Identity security (refresh rotation, lockout), the IDOR/authorization tests, idempotency, saga compensation, the ledger, the Inventory race test. Those *are* the project's value.

---

## 1.18 Security checklist & mini pen-test (do this in Week 10)

🎯 **Goal:** attack **your own** system the way an attacker would. This starts your pen-testing curriculum (architecture doc §6.3) at the right place: defence first, then offence. Use Bruno/curl + browser DevTools. For each row: *run the attack → confirm the expected defence → write a test if you didn't have one.*

| OWASP API risk | Attack to run now | Expected defence |
|---|---|---|
| **API1 BOLA / IDOR** | As customer B request A's `GET /orders/{id}`, `/orders/{id}/tracking`, `POST /orders/{id}/cancel`, `GET /payments/{id}`, `POST /payments/{id}/pay`, `PUT/DELETE /customers/me/addresses/{id}` · As seller B `PUT /seller/products/{A's id}`, `POST /seller/inventory/adjust` on A's variant | **404** every time (never the data, never a 403 that confirms existence) |
| **API2 Broken auth** | Replay an expired JWT · edit the payload (`role`→`super_admin`) and re-send · craft an `alg:none` token · use an old refresh token twice · 6 wrong passwords · call a protected route with no token | 401 each; reuse → whole family revoked; 5th failure → 429 `ACCOUNT_LOCKED` |
| **API3 Mass assignment** | `POST /auth/register` with `"role":"super_admin"` and extra fields like `"emailConfirmed":true` · `PUT /customers/me` with `"userId":"<other>"` | 400 validation error / extra fields ignored |
| **API4 Resource consumption** | `GET /products?page_size=100000` · `POST /cart/items` quantity `999999` · 2 MB JSON body · 100 login attempts/min · 51 distinct cart lines | capped at 50 · 422 · 413 · 429 · 422 `CART_FULL` |
| **API5 Function-level auth** | Customer token on `/admin/sellers/*` · seller token on `/dev/orders/*` · anonymous on `/seller/products` | 403 / 401 |
| **API6 Business-flow abuse** | Place the same order 20× in parallel (same key, then different keys) · spam `resend-otp` | one order for one key · 60 s OTP cooldown · stock reservation cap |
| **API7 SSRF** | Product image URL `http://169.254.169.254/` or `file:///etc/passwd` | `file:`/other schemes rejected by the validator; **Catalog never fetches** the URL (the browser does) — record this: when Media service later *fetches* URLs, SSRF becomes real |
| **API8 Security misconfig / injection** | `?query=' OR 1=1--` · `?query=%25` · very long query · weird Unicode · `GET /api/v1/internal/variants/lookup` via gateway · `/scalar` and `/openapi` in a **non-Development** run · verbose errors | parameterised SQL, escaped wildcards · 404 for internal · docs endpoints absent outside Development · generic 500 messages with `traceId` only |
| **API9 Improper inventory** | List every route (`/openapi/v1.json` in dev) and compare with the gateway route table | nothing reachable that isn't intended; `/dev/*` routes exist **only** in Development config |
| **API10 Unsafe consumption** | Failure lab: bad-signature, amount-mismatch, duplicate webhook · publish a malformed event into a queue by hand (RabbitMQ UI) | 401 / 422 / no-op · message lands in `_error` queue, consumer keeps running |

**Housekeeping checklist**
- [ ] No secret in Git: `git grep -nIiE "password|secret|apikey" -- . ':!*.md' ':!*Development.json' ':!docker-compose*.yml' ':!.env.example'` returns nothing alarming; `keys/` and `.env` ignored
- [ ] Passwords are hashed (look at `identity.users.password_hash`); refresh tokens and OTPs are hashed
- [ ] Card number/CVC never appear in any log (search the Payments log after paying)
- [ ] Every endpoint declares its auth (anonymous endpoints are `AllowAnonymous()` *explicitly*; the fallback policy catches everything you forget)
- [ ] `Development`-only code (dev OTP endpoint, simulator, failure lab, Scalar) is wrapped in `IsDevelopment()`
- [ ] Dependency scan: `dotnet list package --vulnerable --include-transitive`, `npm audit`, `pip install pip-audit && pip-audit` — fix or note each finding
- [ ] Known weak spots written down in `docs/known-gaps.md` (see §1.22 — *an honest gap list is a sign of maturity, not weakness*)

---

## 1.19 Run everything in Docker (the "production-shaped" check)

🎯 **Goal:** `docker compose up` runs **all** services as containers on a private network where **only the gateway is exposed** — the same shape Kubernetes will have in Phase 2.

First, a small fix to the Dockerfile from §0.9: Identity writes its signing key to a mounted folder, and a non-root user needs a writable directory. In the **runtime stage**, add *before* `USER $APP_UID`:
```dockerfile
RUN mkdir -p /keys && chown $APP_UID /keys
```
`src/ml/recommender/Dockerfile` already exists (§1.15). Now `docker-compose.apps.yml`:

```yaml
# Run:  docker compose -f docker-compose.yml -f docker-compose.apps.yml up -d --build
# Inside the compose network, services reach each other by SERVICE NAME (identity, catalog…), not localhost. Container port is always 8080.
x-env: &env
  ASPNETCORE_ENVIRONMENT: Development          # enables dev endpoints + auto-migrate for this local run; staging/prod use their own settings
  Redis__ConnectionString: redis:6379
  RabbitMq__Host: rabbitmq
  RabbitMq__Username: lankamart
  RabbitMq__Password: lankamart_dev_pw
  Auth__Authority: http://identity:8080
  Auth__RequireHttps: "false"
  Internal__ApiKey: dev-internal-key-change-me
  Otel__Endpoint: ""                           # set to http://jaeger:4317 if you run the 'obs' profile
  Services__Catalog: http://catalog:8080
  Services__Inventory: http://inventory:8080
  Services__Payments: http://payments:8080

x-app: &app
  restart: unless-stopped
  depends_on:
    postgres: { condition: service_healthy }
    redis: { condition: service_healthy }
    rabbitmq: { condition: service_healthy }

services:
  gateway:
    <<: *app
    build: { context: ., dockerfile: infra/docker/dotnet.Dockerfile, args: { PROJECT: src/gateway/LankaMart.Gateway/LankaMart.Gateway.csproj, DLL: LankaMart.Gateway.dll } }
    ports: ["5100:8080"]                       # ← the ONLY published port
    environment:
      <<: *env
      ReverseProxy__Clusters__identity__Destinations__d1__Address: http://identity:8080/
      ReverseProxy__Clusters__users__Destinations__d1__Address: http://users:8080/
      ReverseProxy__Clusters__catalog__Destinations__d1__Address: http://catalog:8080/
      ReverseProxy__Clusters__inventory__Destinations__d1__Address: http://inventory:8080/
      ReverseProxy__Clusters__orders__Destinations__d1__Address: http://orders:8080/
      ReverseProxy__Clusters__payments__Destinations__d1__Address: http://payments:8080/
      ReverseProxy__Clusters__recommender__Destinations__d1__Address: http://recommender:8080/
      Cors__Origins__0: http://localhost:5173

  identity:
    <<: *app
    build: { context: ., dockerfile: infra/docker/dotnet.Dockerfile, args: { PROJECT: src/services/identity/LankaMart.Identity.Api/LankaMart.Identity.Api.csproj, DLL: LankaMart.Identity.Api.dll } }
    volumes: [jwtkeys:/keys]                   # the signing key survives container recreation
    environment:
      <<: *env
      ConnectionStrings__Default: Host=postgres;Database=lankamart;Username=identity_svc;Password=identity_dev_pw
      Jwt__KeyPath: /keys/jwt-private.pem

  users:
    <<: *app
    build: { context: ., dockerfile: infra/docker/dotnet.Dockerfile, args: { PROJECT: src/services/users/LankaMart.Users.Api/LankaMart.Users.Api.csproj, DLL: LankaMart.Users.Api.dll } }
    environment: { <<: *env, ConnectionStrings__Default: "Host=postgres;Database=lankamart;Username=users_svc;Password=users_dev_pw" }

  catalog:
    <<: *app
    build: { context: ., dockerfile: infra/docker/dotnet.Dockerfile, args: { PROJECT: src/services/catalog/LankaMart.Catalog.Api/LankaMart.Catalog.Api.csproj, DLL: LankaMart.Catalog.Api.dll } }
    environment: { <<: *env, ConnectionStrings__Default: "Host=postgres;Database=lankamart;Username=catalog_svc;Password=catalog_dev_pw" }

  inventory:
    <<: *app
    build: { context: ., dockerfile: infra/docker/dotnet.Dockerfile, args: { PROJECT: src/services/inventory/LankaMart.Inventory.Api/LankaMart.Inventory.Api.csproj, DLL: LankaMart.Inventory.Api.dll } }
    environment: { <<: *env, ConnectionStrings__Default: "Host=postgres;Database=lankamart;Username=inventory_svc;Password=inventory_dev_pw", Inventory__DevDefaultStock: "50" }

  orders:
    <<: *app
    build: { context: ., dockerfile: infra/docker/dotnet.Dockerfile, args: { PROJECT: src/services/orders/LankaMart.Orders.Api/LankaMart.Orders.Api.csproj, DLL: LankaMart.Orders.Api.dll } }
    environment: { <<: *env, ConnectionStrings__Default: "Host=postgres;Database=lankamart;Username=orders_svc;Password=orders_dev_pw" }

  payments:
    <<: *app
    build: { context: ., dockerfile: infra/docker/dotnet.Dockerfile, args: { PROJECT: src/services/payments/LankaMart.Payments.Api/LankaMart.Payments.Api.csproj, DLL: LankaMart.Payments.Api.dll } }
    environment:
      <<: *env
      ConnectionStrings__Default: Host=postgres;Database=lankamart;Username=payments_svc;Password=payments_dev_pw
      Mock__WebhookSecret: dev-mock-webhook-secret-change-me
      Mock__WebhookUrl: http://payments:8080/api/v1/webhooks/mock

  recommender:
    restart: unless-stopped
    build: ./src/ml/recommender
    environment: { CATALOG_URL: "http://gateway:8080/api/v1" }
    depends_on: [gateway]

volumes:
  jwtkeys:
```
✅ **Check**
1. Stop `python scripts/dev.py` (free the ports), then `docker compose -f docker-compose.yml -f docker-compose.apps.yml up -d --build`; `docker compose ps` → all healthy/running; `docker images | grep lankamart` → note the **sizes**.
2. `python scripts/seed_demo.py` works unchanged (it only talks to `localhost:5100`). `npm run dev` for React works unchanged.
3. **Isolation proof:** `curl http://localhost:5101/api/v1/ping` → *connection refused* (Identity is not published). Only `:5100` answers.
4. `docker compose logs -f orders` while placing an order: JSON log lines with `service` and `trace_id`. `docker compose stop payments` → place an order → 503 + compensation; `start payments` → recovers (**your first chaos test**).
5. `docker compose down` (keeps data) / `down -v` (wipes everything). Run the full buying flow once more from a *clean* start — this is your "works on a fresh machine" proof.

⚠️ **Pitfalls:** *services restart-looping at start* → Postgres still initialising (they retry migrations 10×; check `docker compose logs <svc>`) · *Identity can't write the key* → the `/keys` Dockerfile line is missing · *401 everywhere* → `Auth__Authority` must be `http://identity:8080` **inside** the network, and Identity's own `Auth__Authority` must match what it advertises in `jwks_uri` · don't commit real secrets into compose files — these are throwaway dev values.

Commit: `chore(docker): compose apps overlay, only gateway exposed`.

---

## 1.20 PHASE 1 GATE — definition of done

Tick every box. This is the contract with your future self.

**Functionality**
- [ ] Customer registers, verifies email + phone (OTP), logs in, token refresh works, logout kills the token immediately
- [ ] Seller registers → verifies → submits KYC → Seller Manager approves → seller creates products with variants → publishes → visible in search
- [ ] Guest cart persists; merges on login
- [ ] Checkout creates one order per Idempotency-Key; totals computed server-side from Catalog prices; address snapshotted
- [ ] Fake payment: `4111…` succeeds (order `Paid`, stock decremented, ledger balanced), `4000…0002` declines (stock released, cart restored), `.99` total goes pending then succeeds
- [ ] Unpaid orders expire after 15 min and release stock; customers can cancel while unpaid
- [ ] Order tracking timeline shows every status change (dev simulator can advance to `Completed`)
- [ ] React storefront does all of the above; Flutter slice logs in and browses; recommender returns related products

**Engineering quality**
- [ ] Each service owns its schema; per-service DB logins; no cross-schema access (isolation test from §0.6 still fails with *permission denied*)
- [ ] All inter-service writes are events; sync calls only for the three saga queries (price lookup, reserve, intent) — all with internal key + resilience policies
- [ ] Every consumer is idempotent (tested with double delivery)
- [ ] `/api/v1/internal/*` unreachable through the gateway
- [ ] Mandatory test list (§1.16.7) done; CI green on `main` (dotnet + web + python)
- [ ] Compensation paths proven by tests **and** by the manual chaos test (§1.19 check 4)
- [ ] Security pass (§1.18) done; `docs/known-gaps.md` written
- [ ] Everything runs with `docker compose up` on a clean machine; only the gateway port is published
- [ ] ADRs updated (add: internal API key, per-service DB roles, explicit saga, Redis+Postgres inventory)
- [ ] README quick-start verified by following it literally

**The 10-minute demo script** (record it as a video for your portfolio):
1. *(1 min)* Show the architecture diagram and the `docker compose ps` output.
2. *(2 min)* Seller registers → KYC → manager approves → seller lists a product (watch RabbitMQ and `known_sellers` update).
3. *(3 min)* Customer browses (Sinhala-ready i18n keys), adds to cart as guest, registers/verifies/logs in, checks out, pays with `4111…`, sees `Paid` + timeline.
4. *(2 min)* Failure story: decline card → stock/cart restored; stop Payments → compensation; Jaeger trace of a checkout.
5. *(1 min)* Show the race test (200 buyers / 50 units) passing and the balanced ledger query.
6. *(1 min)* Say what's next (Phase 2: outbox, DB split, Kubernetes).

**Self-quiz — answer aloud before declaring Phase 1 done**
1. Why does Catalog keep a copy of seller status instead of calling Users? What happens if Users is down?
2. What is the exact failure scenario the Outbox pattern fixes? Where in your code does that scenario exist today?
3. Why do we use `UPDATE … WHERE status = 'X'` instead of load–modify–save for order transitions?
4. Explain why the reservation TTL (17 min) is longer than the payment window (15 min).
5. A customer double-clicks *Pay*. Name every mechanism that prevents two charges.
6. Why must the webhook signature be computed over the raw body?
7. Why return 404 instead of 403 for another user's order?
8. What happens if the same `OrderPaid` event is delivered twice?
9. Why can't you just `UPDATE` a wrong ledger row?
10. Why is the Redis stock counter decremented at *reserve* time, and why does *confirm* not touch Redis?

When all boxes are ticked: `git tag phase-1-complete && git push --tags`, write the retrospective in `docs/retro-phase-1.md` (*what went well / what hurt / what I'd do differently / estimate accuracy*), and take a day off.

---

## 1.21 Phase 1 troubleshooting

| Symptom | Likely cause → fix |
|---|---|
| Registered user has no profile (`PROFILE_NOT_READY` persists) | Users service not running/consuming; check RabbitMQ UI queue `users-user-registered` (messages stuck = consumer down; message in `…_error` = consumer threw — read its headers) |
| `401` from every service but Identity login works | `Auth:Authority` wrong, or Identity isn't reachable from that service; `Auth:Issuer/Audience` mismatch; clock skew in containers |
| `403 SELLER_NOT_APPROVED` right after approval | Event not yet processed — wait a second (eventual consistency) or check Catalog consumer logs |
| `409 REQUEST_IN_PROGRESS` | An earlier checkout with that idempotency key crashed mid-way; wait (key TTL) or use a new key |
| Checkout 503 `INVENTORY_UNAVAILABLE` | Inventory down or **internal key mismatch** (`Internal:ApiKey` must be identical everywhere) |
| Stock never goes back after cancel | `OrderCancelled` consumer not running in Inventory; sweeper still frees it after the TTL |
| Redis stock ≠ Postgres available | Known drift cases (crash between Redis and PG writes); fix by deleting `stock:{id}` (it reloads from Postgres) — the Phase 2 reconciler automates this |
| Payment stays `Processing` | Webhook never arrived: `Mock:WebhookUrl` wrong, signature secret mismatch, or Payments restarted (jobs are in memory) |
| `.99` order never completes | Wait 30 s; check the Payments log for "Mock webhook … → HTTP 200" |
| EF: *relation "…" does not exist* | Migration not applied (service didn't start, or wrong DB login/schema) |
| EF: *permission denied for schema* | Using the wrong `*_svc` login for that service |
| `dotnet ef` can't find the DbContext | Add `--project <path>` and make sure the project builds; `Program` must have `public partial class Program;` |
| MassTransit can't connect | RabbitMQ not healthy or wrong credentials; check `docker compose ps` |
| React: random logouts | Parallel refreshes (single-flight missing) → server saw "token reuse" and revoked the family |
| React: CORS error | Not using the Vite proxy / relative URLs |
| Flutter: connection refused | Wrong base URL for emulator (use `10.0.2.2`) |
| Tests: Testcontainers can't start | Docker not running / not enough RAM; in CI use `ubuntu-latest` |
| Everything slow | Too many services + Android emulator for your RAM: run only what you need (`python scripts/dev.py gateway identity users catalog`) |

---

## 1.22 What comes next — Phase 2 preview and the Known-Gaps register

**Phase 2 — Split & Scale (roadmap Weeks 11–16)** will: split schemas into true separate databases (trivial thanks to per-service logins) · add the **Transactional Outbox** to every service · build the **Notification service** (replacing the logged OTPs and giving customers real emails/SMS/push) · add the **inventory reconciler** and other workers · move to **Kubernetes** (k3s + Helm + ArgoCD, health probes you already have, NetworkPolicies) · run **k6** load tests including the 10k-user flash sale · and decide the **MassTransit v8 → v9/alternative** question (ADR-003) before launch.

**Known-gaps register** (copy into `docs/known-gaps.md` — each is a deliberate Phase 1 shortcut, with the phase that fixes it):

| # | Gap | Risk today | Fixed in |
|---|---|---|---|
| 1 | Publish-after-commit (dual write) everywhere | lost event after a crash at the wrong millisecond | Phase 2 (outbox) |
| 2 | OTPs are logged, not sent | dev-only | Phase 2 (Notification) |
| 3 | Shared internal API key | one leaked key opens all internal APIs | Phase 6 (mTLS / mesh) |
| 4 | Refresh token in `localStorage` (web) | XSS could steal it | Phase 6 (httpOnly cookie) |
| 5 | Mock webhook jobs held in memory | lost on restart (mock only) | n/a (real gateway retries) |
| 6 | Redis↔Postgres stock drift only self-heals via TTL | rare mis-counts | Phase 2 (reconciler) |
| 7 | KYC data (NIC, bank no.) stored in clear text | PII exposure | Phase 3/6 (encryption + Vault) |
| 8 | No MFA for sellers/admins | account takeover | Phase 3/6 |
| 9 | Client IP from `X-Forwarded-For` is spoofable if services are reachable directly | audit/rate-limit evasion | Phase 6 (NetworkPolicy) |
| 10 | Shipments mirror the order status | multi-seller orders can't progress independently | Phase 4 (Logistics) |
| 11 | Single currency, flat delivery fee | — | Phase 4/5 |
| 12 | Auto-migrate on startup | races with multiple replicas | Phase 2 (migration job) |
| 13 | Gateway cannot revoke by user (only by `jti`) | suspended user keeps token ≤15 min | Phase 6 |

> **Final word of Phase 0–1.** You are not building a website; you are building a *system that stays correct when things fail*. If by the end you can explain §1.20's ten questions, you understand more distributed-systems engineering than most developers with years of experience. Keep the journal, keep the commits small, and keep the tests honest.

---

# APPENDICES

## Appendix A — Ports & URLs

| Component | Local URL / port | Notes |
|---|---|---|
| **Gateway** | `http://localhost:5100` | the only door clients use |
| Identity | `:5101` | `/.well-known/jwks.json`, `/scalar/v1` (dev) |
| Users | `:5102` | |
| Catalog | `:5103` | |
| Inventory | `:5104` | |
| Orders | `:5105` | |
| Payments | `:5106` | webhook target for the mock gateway |
| Recommender (Python) | `:5110` | `/health`, `/docs` (FastAPI Swagger) |
| React dev server | `http://localhost:5173` | proxies `/api` → `:5100` |
| PostgreSQL | `:5432` | db `lankamart` |
| Redis | `:6379` | |
| RabbitMQ | `:5672` / UI `http://localhost:15672` | `lankamart / lankamart_dev_pw` |
| pgAdmin (profile `tools`) | `http://localhost:5050` | `admin@lankamart.local / admin_dev_pw` |
| Jaeger (profile `obs`) | `http://localhost:16686` | OTLP `:4317` |
| Inside Docker network | every .NET service listens on `8080` | addressed by service name |

## Appendix B — Dev credentials & configuration (all throwaway, local-only)

| What | Value |
|---|---|
| Postgres superuser | `postgres / postgres_dev_pw` |
| Service DB logins | `identity_svc / identity_dev_pw`, `users_svc / users_dev_pw`, `catalog_svc / catalog_dev_pw`, `inventory_svc / inventory_dev_pw`, `orders_svc / orders_dev_pw`, `payments_svc / payments_dev_pw` |
| RabbitMQ | `lankamart / lankamart_dev_pw` |
| Internal API key | `dev-internal-key-change-me` (header `X-Internal-Key`) |
| Mock webhook secret | `dev-mock-webhook-secret-change-me` |
| Super Admin | `admin@lankamart.local / Admin#12345678` |
| Seller Manager | `sellermgr@lankamart.local / Manager#12345678` |
| Demo seller / customer (after seeding) | `seller1@lankamart.local`, `customer1@lankamart.local` — password `Sup3rSecret!!` |
| Fake cards | `4111 1111 1111 1111` ✅ · `4000 0000 0000 0002` ❌ declined · `4000 0000 0000 0119` ❌ gateway error · total ending `.99` ⏳ 30 s |

**Per-service config differences**

| Service | DB login | Extra config keys |
|---|---|---|
| Gateway | — | `Cors:Origins`, `ReverseProxy:*`, `Redis`, `Auth` |
| Identity | `identity_svc` | `Jwt:{KeyPath,AccessTokenMinutes,RefreshTokenDays}`, `Seed:*`, `Auth:Authority` = its own URL |
| Users | `users_svc` | — |
| Catalog | `catalog_svc` | `Catalog:ModerationEnabled` (false until Phase 3) |
| Inventory | `inventory_svc` | `Inventory:DevDefaultStock` (50 in dev) |
| Orders | `orders_svc` | `Orders:*`, `Services:{Catalog,Inventory,Payments}` |
| Payments | `payments_svc` | `Mock:*` |

**Production rule:** none of the values above are ever used outside your laptop. Staging/production secrets come from a secret manager (Phase 6) and environment variables, never from Git.

## Appendix C — REST endpoint reference (Phase 1)

| Service | Method & path | Auth |
|---|---|---|
| Identity | `POST /auth/register` · `/verify-otp` · `/resend-otp` · `/login` · `/refresh` | anonymous (rate-limited) |
| | `POST /auth/logout` · `GET /auth/me` | token |
| Users | `GET/PUT /customers/me` · `GET/POST /customers/me/addresses` · `PUT/DELETE …/{id}` | customer |
| | `GET /sellers/me` · `POST /sellers/kyc` | seller (verified for KYC) |
| | `GET /admin/sellers` · `GET /admin/sellers/{id}` · `POST …/{id}/start-review · approve · reject · suspend · reinstate` | seller_manager, super_admin |
| Catalog | `GET /products` · `GET /products/{slug}` · `GET /categories` | anonymous |
| | `GET/POST /seller/products` · `PUT /seller/products/{id}` · `POST …/{id}/variants` · `PUT …/{id}/variants/{vid}` · `POST …/{id}/publish · archive` | approved seller |
| Inventory | `GET /inventory/availability?variantIds=` | anonymous |
| | `GET /seller/inventory` · `POST /seller/inventory/adjust` | seller |
| Orders | `GET /cart` · `POST /cart/items` · `PATCH/DELETE /cart/items/{variantId}` | guest or user |
| | `POST /cart/merge` | user |
| | `POST /orders` (+`Idempotency-Key`) · `GET /orders` · `GET /orders/{id}` · `GET /orders/{id}/tracking` · `POST /orders/{id}/cancel` | verified customer / owner |
| Payments | `GET /payments/{id}` · `POST /payments/{id}/pay` | owner |
| | `POST /webhooks/mock` | HMAC signature |
| Recommender | `GET /recommendations/related/{productId}?limit=` | anonymous |
| **Internal (not via gateway)** | `POST /internal/variants/lookup` (Catalog) · `POST /internal/inventory/reserve · /confirm/{id} · /release/{id}` · `POST /internal/payments/intent` | `X-Internal-Key` |
| **Dev only** | `GET /dev/otp/{destination}` · `POST /dev/orders/{id}/advance` · `POST /dev/mock/{paymentId}/duplicate-webhook · bad-signature · amount-mismatch` | Development environment |

*(All paths are prefixed `/api/v1`.)*

## Appendix D — Event catalog (Phase 1)

| Event (`LankaMart.Contracts.V1`) | Producer | Consumers | Purpose |
|---|---|---|---|
| `UserRegistered` | Identity | Users | create profile |
| `UserContactVerified` | Identity | *(Phase 2: Notification)* | |
| `UserLoginFailed` | Identity | *(Phase 6: Security)* | brute-force signal |
| `SellerKycSubmitted` | Users | *(Phase 2: Notification)* | notify managers |
| `SellerApproved / Rejected / Suspended / Reinstated` | Users | Catalog | maintain `known_sellers` |
| `VariantCreated` | Catalog | Inventory | create stock row |
| `ProductStatusChanged` | Catalog | *(Phase 3: search indexer)* | |
| `OrderCreated` | Orders | *(Phase 2/5: Notification, Analytics)* | |
| `OrderPaid` | Orders | Inventory | confirm reservation |
| `OrderCancelled` | Orders | Inventory | release reservation |
| `OrderStatusChanged` | Orders | *(Phase 2: Notification)* | |
| `OrderPaymentOrphaned` | Orders | Payments | flag refund |
| `PaymentCompleted` | Payments | Orders | mark order paid |
| `PaymentFailed` | Payments | Orders | compensate |
| `InventoryLowStock` | Inventory | *(Phase 2: Notification)* | |
| `ReservationConfirmFailed` | Inventory | Orders | oversold → refund pending |

## Appendix E — Command cheat-sheet

```bash
# ---- Docker ----
docker compose up -d                              # infra
docker compose --profile tools --profile obs up -d
docker compose -f docker-compose.yml -f docker-compose.apps.yml up -d --build   # everything in containers
docker compose ps | logs -f <svc> | stop | down | down -v      # (down -v DELETES DATA)
docker exec -it lm-postgres psql -U postgres -d lankamart
docker exec -it lm-redis redis-cli                # KEYS stock:*  |  GET stock:<id>  |  HGETALL cart:u:<id>

# ---- .NET ----
dotnet build | test | run --project <path> --no-launch-profile
dotnet watch run --project <path>                 # hot reload
dotnet ef migrations add <Name> --project <svc project> --output-dir Data/Migrations
dotnet ef migrations remove --project <svc project>              # undo the last UNAPPLIED migration
dotnet ef database update --project <svc project>
dotnet list package --outdated | --vulnerable --include-transitive

# ---- Git ----
git switch -c feat/<name> ; git add -p ; git commit -m "feat(scope): msg" ; git push -u origin HEAD
git tag phase-1-complete && git push --tags

# ---- Node / Flutter / Python ----
npm run dev | build | lint ;   flutter run | pub get | doctor ;   pytest -q ;   uvicorn app.main:app --port 5110 --reload
```

## Appendix F — SOAP: when you actually need it, and how

**Honest summary:** *LankaMart's own APIs are REST/JSON (and events). You do **not** build SOAP services. You may one day **consume** a SOAP service because a partner only offers one.* SOAP is an XML-based protocol with a formal contract (**WSDL**) and standards like WS-Security; it is common in older **bank, government, telecom and courier** systems. Nothing in Phases 0–1 needs it.

| | REST/JSON | SOAP/XML | gRPC |
|---|---|---|---|
| Used here for | all public & internal APIs | only if a partner forces it | optional later for internal high-throughput calls |
| Contract | OpenAPI | WSDL | `.proto` |
| Typical in | PayHere, FCM, SMS aggregators, most modern APIs | legacy bank/government/courier integrations | internal microservice calls |

**If a SOAP partner appears (e.g., a courier's booking API in Phase 4, or a bank's statement service):**

1. **Isolate it behind an adapter** (an "anti-corruption layer") so the rest of your code never sees XML:
```csharp
public interface ICourierGateway { Task<string> BookPickupAsync(PickupRequest r, CancellationToken ct); }   // YOUR clean interface
public sealed class SoapCourierGateway : ICourierGateway { /* the only class that knows about SOAP */ }
```
2. **Generate a client from the WSDL** with Microsoft's tool:
```bash
dotnet tool install --global dotnet-svcutil
dotnet-svcutil https://partner.example/CourierService?wsdl -n "*,Partner.Soap" -d src/services/logistics/Connected/
```
This creates typed C# classes and a `…Client` (class names depend on the WSDL) and adds the `System.ServiceModel.*` packages.
3. **Call it** (names illustrative):
```csharp
var binding = new BasicHttpBinding(BasicHttpSecurityMode.Transport) { SendTimeout = TimeSpan.FromSeconds(10) };
var client = new Partner.Soap.CourierServiceClient(binding, new EndpointAddress(config["Courier:Url"]));
var response = await client.BookPickupAsync(new BookPickupRequest { /* map from your PickupRequest */ });
```
4. **Wrap it in resilience** (timeouts, retry only if the operation is idempotent, circuit breaker) — SOAP clients don't get the `HttpClient` resilience handler automatically; use a `ResiliencePipeline` (Polly v8) around the call.
5. **Test with a stub:** run the partner's sandbox, or mock the SOAP endpoint with WireMock; never test against production.
6. **Security:** SOAP often uses **WS-Security** (username token/certificates) — keep credentials in secrets, validate the partner's TLS certificate, log **no** XML bodies containing personal data.
7. **Exposing SOAP yourself** (only if *they* require your system to be a SOAP server) would use **CoreWCF** — decide that when it happens; don't build it speculatively.

**Rule of thumb:** REST for everything you control; an adapter + generated client for anything SOAP you must consume.

## Appendix G — Glossary

**ACID** reliable DB transactions · **API Gateway** single front door that routes/limits/authenticates · **At-least-once** a message may arrive twice · **BOLA/IDOR** reading someone else's object by guessing an id · **Circuit breaker** stops calling a failing dependency for a while · **Claim** a fact inside a JWT · **Compare-and-set** `UPDATE … WHERE state = expected` · **Compensation** an action that undoes an earlier saga step · **Consumer** code that handles a message · **CQRS** separating reads from writes · **DLQ / error queue** where failed messages wait · **Double-entry ledger** every money movement has equal debits and credits · **DTO** data-transfer object · **Eventual consistency** services agree *soon*, not instantly · **Escrow** platform holds buyer money until delivery · **Idempotent** safe to repeat · **JWT / JWKS** signed token / public-key set · **KYC** know-your-customer verification · **Migration** versioned DB schema change · **Outbox** write event to DB with the data, publish later (Phase 2) · **Process manager / saga** a multi-step workflow with compensations · **RBAC** role-based access control · **Read model** a local copy of another service's data built from events · **Refresh token** long-lived credential to get new access tokens · **Snapshot** frozen copy of data inside a record · **TTL** time-to-live · **WSDL** SOAP's contract file.

## Appendix H — Decision log (summary; write each as an ADR)

| ADR | Decision | Reason in one line |
|---|---|---|
| 001 | .NET 10 LTS | .NET 8/9 support ends 10 Nov 2026 |
| 002 | No MediatR/AutoMapper; plain feature classes | commercial licences; no learning value lost |
| 003 | MassTransit **v8.x** now; decide v9-vs-alternative before launch | v9 commercial; v8 free but maintenance ends 2026 |
| 004 | Hand-built JWT/refresh before OpenIddict | learn what OIDC servers automate; API contract unchanged |
| 005 | One Postgres, schema + login per service | enforces "no cross-service DB access" and eases Phase 2 split |
| 006 | Postgres `ILIKE`+`pg_trgm` now; Meilisearch in Phase 3 | don't run infra you can't use yet |
| 007 | Object storage choice deferred; MinIO excluded | MinIO community edition archived (Apr 2026) |
| 008 | Orchestrated saga with order-row state + compare-and-set + sweepers | readable, testable, teaches compensation |
| 009 | Inventory: Redis counters (Lua) + Postgres truth, sweeper from Postgres | atomic hot path, durable recovery |
| 010 | Catalog keeps a `known_sellers` read-model from events | no sync dependency on Users |
| 011 | Internal API key for `/internal/*`, never routed by gateway | simple now; mTLS in Phase 6 |
| 012 | Fake gateway behaves like a real one (async signed webhooks) | PayHere becomes one adapter |
| 013 | Cart in Redis, prices never stored in it | always price from Catalog at view/checkout time |
| 014 | Vite SPA now, SSR in Phase 3 | SEO matters later; API-first keeps the move cheap |
| 015 | Python service reads only the public Catalog API | Principle #1 applies to every language |
| 016 | SOAP only via adapter if a partner requires it | nothing in scope needs it |

---

# CONCLUSION

**Where you are after Phase 0:** a professional-grade foundation — monorepo, pinned SDK, containerised infrastructure with isolated per-service database logins, a shared toolbox that makes every service log, trace, fail and authenticate identically, a gateway "spine", CI that has been green since day one, and your first Dockerfile.

**Where you are after Phase 1:** a working, demo-able marketplace core — verified accounts, an approval-gated seller workflow, a searchable catalog, race-safe inventory, a checkout saga that **compensates** instead of corrupting data, an idempotent order API, a fake payment gateway that behaves like a real one, a balanced double-entry ledger, web and mobile clients, a Python algorithm service, tests on every dangerous path, and the whole thing running in containers with a single exposed port.

**The real deliverable** is not the code — it is that you can now explain *why* each piece exists: why the order row is the saga's memory, why a webhook is untrusted until verified, why a 404 beats a 403, why a cart never stores prices, and why you wrote down your known gaps. Those are the instincts that separate someone who followed tutorials from someone who has built and *operated* a distributed system.

**First action after reading this document:** do **§0.2** — install and verify the tools, then create the repo. Every system starts as a request that returns `200 OK`.

— *End of the Phase 0 & Phase 1 guide* —