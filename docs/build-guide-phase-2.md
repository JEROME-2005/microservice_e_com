# LankaMart — Phase 2: Split & Scale (Weeks 11–16)

**Builds on:** the *Phase 0 & Phase 1 guide* (tag `phase-1-complete` must exist and its gate must be ticked).
**Roadmap definition (architecture doc §9):** *Split shared dev DB → DB-per-service + migrations strategy · Outbox on all Phase 1 services; retire direct publishes · Notification Service (email + in-app; FCM after) · Inventory sweeper + reconciler workers · Kubernetes migration: k3s staging, Helm charts, ArgoCD, health probes, NetworkPolicies · k6 load suite + HPA tuning. **Gate: staging deploys via GitOps; flash-sale load test passes; services independently deployable.***
**Written:** 10 November 2026.

---

## HOW TO USE THIS DOCUMENT
Same format as before: 🎯 Goal · 🧠 Why · 🛠️ Do · ✅ Check · ⚠️ Pitfalls. Same rules: type the code, one step = one commit, never move on while a ✅ fails. Realistic time: **6 sprints ≈ 6 weeks at 15–20 h/week; as a beginner plan for 9.** The Kubernetes sprint is the steepest learning curve of the whole project — budget extra time there and don't be discouraged; everyone finds it hard at first.

**Sprint map**

| Sprint | Topic | You can demo afterwards |
|---|---|---|
| **1** | True **database-per-service** + migration jobs | six services, six databases, `permission denied` across them; `--migrate-only` mode |
| **2** | **Transactional Outbox + Inbox** everywhere | kill a service mid-request → no lost events, no duplicate effects |
| **3** | **Notification service** (email, in-app, SMS/push adapters) | real emails (Mailpit), in-app bell in React, retries & dead-letter |
| **4** | **Reconciler** + workers split | induce Redis/Postgres drift → detected and healed |
| **5** | **Kubernetes**: k3s, Helm, NetworkPolicies, Argo CD, CI → GitOps | `git push` → images built, scanned, deployed automatically |
| **6** | **k6 load tests + HPA tuning** | flash-sale test, no oversell, autoscaling in action |

---

# PART A — DECISIONS & CORRECTIONS (checked 10 Nov 2026)

| # | Topic | Architecture doc said | This guide does | Why |
|---|---|---|---|---|
| 1 | **Ingress** | (implicit) NGINX ingress | **Traefik (bundled with k3s)**; Gateway API noted as the future path | The Kubernetes **ingress-nginx project was retired in March 2026** — no more releases or security fixes. Don't install it. Your own YARP gateway does the clever routing anyway; the cluster only needs a simple front door. |
| 2 | **Databases/queues as Helm charts** | (not specified) | **Plain manifests with official images** (`postgres`, `redis`, `rabbitmq`) for staging | Bitnami stopped publishing its free versioned images/charts to Docker Hub in Aug 2025 (moved to a no-updates "legacy" repo; full catalog is a paid subscription). A tutorial built on Bitnami charts would break. In production you'd use managed services or operators (Phase 6/7). |
| 3 | **Outbox** | MassTransit EF outbox | Same — **MassTransit v8** `MassTransit.EntityFrameworkCore` | Still free in v8. ADR-003's pre-launch licence decision stands. |
| 4 | **Migrations in prod** | "migrations strategy" | Same image run with **`--migrate-only`** as a Kubernetes/Argo **PreSync Job**; app auto-migrate only in Development | One artefact (the image), no extra tooling; safe with several replicas. |
| 5 | **Secrets** | Vault/Sealed Secrets (Phase 6) | Kubernetes `Secret`s created by a script, **never in Git**; sealing/Vault in Phase 6 | Keeps Phase 2 about delivery, not secret-management tooling. |
| 6 | **Email/SMS vendors** | SendGrid/SES, local SMS gateway | **SMTP via MailKit** (works with Mailpit locally and with SendGrid/SES SMTP relays in prod) + adapter interfaces for SMS/push | No vendor SDK lock-in; one code path; swap by config. |
| 7 | **Helm layout** | one chart per service + library chart | **one generic chart + one values file per service** | Same result, far less YAML for a beginner. |
| 8 | **Observability** | Prometheus/Grafana/Loki | **Jaeger traces + `kubectl top` + k6 summaries** now; full Prometheus/Grafana stack in Phase 6 | HPA needs only metrics-server (bundled in k3s). Avoid running 6 more systems on a laptop. |

> **Red-flag box.** Keep `MassTransit*` at `--version "8.*"`. Third-party GitHub Actions must be **pinned** (to a release tag you checked, or a full commit SHA) — a moved tag is a supply-chain attack vector.

---

# PART B — CONCEPTS YOU NEED FOR PHASE 2

### B.1 The dual-write problem and the Transactional Outbox
Phase 1 does *"save to DB, then publish event"*. If the process dies between the two, the DB says "paid" but nobody was told. **Outbox fix:** in the **same database transaction** as the business change, insert the event into an `outbox_message` table. A background **delivery service** reads that table and publishes to RabbitMQ, then marks the row delivered. Crash before commit → neither exists. Crash after commit → the row is still there and will be delivered. *The event is now as durable as the data.*

### B.2 The Inbox and "exactly-once effect"
RabbitMQ can still deliver a message twice (outbox delivery is also at-least-once). The **inbox** records each processed `(message id, consumer)`; a duplicate is recognised and skipped — *inside the same transaction as the consumer's own database changes*. At-least-once delivery + idempotent processing = **exactly-once effect**. (You still keep handlers naturally idempotent: belt and braces.)

### B.3 Kubernetes in 12 lines
**Cluster** = machines running containers. **Node** = one machine. **Pod** = one or more containers scheduled together (usually one). **Deployment** = "keep N identical pods running; roll out new versions gradually". **Service** = a stable name/IP that load-balances to a deployment's pods (`http://catalog` just works). **Ingress** = rules letting outside traffic in. **ConfigMap/Secret** = configuration / sensitive configuration injected as env vars or files. **Probes** = *liveness* (restart me if this fails), *readiness* (send me traffic only if this passes), *startup* (give me time to boot). **HPA** = adds/removes pods based on load. **PDB** = "never take down more than X pods at once during maintenance". **NetworkPolicy** = firewall between pods. **Namespace** = a folder that groups and isolates resources. **Job** = run something once to completion (our migrations).

### B.4 Helm and GitOps
**Helm** = a template engine for Kubernetes YAML: one *chart* (templates) + *values* (settings) → many near-identical deployments. **GitOps** = *Git is the single source of truth for what runs.* You never `kubectl apply` by hand in staging; **Argo CD** watches the repo and makes the cluster match it. Benefits: every change is a commit (reviewable, revertible), drift is corrected automatically, **rollback = `git revert`** (architecture doc §7.3).

### B.5 Load testing vocabulary
**VU** = virtual user · **RPS** = requests/second · **p95/p99** = the latency 95 %/99 % of requests beat (averages hide pain; tail latency is what users feel) · **SLO** = a target ("p99 browse < 800 ms, checkout availability 99.9 %") · **ramp-up** = increasing load gradually · **soak** = long steady load to find leaks · **spike** = sudden surge (a flash sale). A load test without *correctness checks* (no overselling, ledger balanced) only measures speed, not safety.

---

# PART C — SPRINT 1: TRUE DATABASE-PER-SERVICE (Week 11)

🎯 **Goal:** each service gets its **own database** (still one PostgreSQL *server* in dev/staging), its own login that can connect **only** to it, and migrations that run as a **separate step** — not at app start in production.

🧠 **Why now, and why is it easy?** In Phase 1 you used one DB with a schema + login per service. Because every service already talks only to its own schema with its own login, "splitting" means: create a database per service, move the schema in, change **one connection-string field**. No code changes to queries. *That is the payoff of the Phase 1 design.* Why bother? (1) a runaway query/migration lock in one database can't stall the others, (2) each can later move to its own server or managed instance independently, (3) backups/restores per service, (4) stronger blast-radius limits.

### 1.1 New init script (replaces `infra/postgres/init/01-init.sql`)

Delete the old `.sql` file and create **`infra/postgres/init/01-databases.sh`** (the `.sh` lets staging inject real passwords from environment variables):

```bash
#!/bin/bash
# Runs once on first start of an EMPTY data volume. Creates one ROLE and one DATABASE per service.
# Dev passwords default to "<svc>_dev_pw"; staging passes DB_PASSWORD_<SVC> via a Kubernetes Secret.
set -euo pipefail

for svc in identity users catalog inventory orders payments notifications; do
  up=$(echo "$svc" | tr '[:lower:]' '[:upper:]')
  eval "pw=\${DB_PASSWORD_${up}:-${svc}_dev_pw}"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<SQL
    CREATE ROLE ${svc}_svc LOGIN PASSWORD '${pw}';
    CREATE DATABASE ${svc}_db OWNER ${svc}_svc;
    REVOKE CONNECT ON DATABASE ${svc}_db FROM PUBLIC;   -- nobody but the owner (and superuser) may even connect
SQL
done

# pg_trgm (fast ILIKE search) must be installed by a superuser, inside the catalog database
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname catalog_db -c "CREATE EXTENSION IF NOT EXISTS pg_trgm;"
```
Also change `POSTGRES_DB` in `.env`/compose to `postgres` (the default admin DB). Because the database **owner** may create schemas, the old `GRANT CREATE ON DATABASE` hack is gone — EF's `HasDefaultSchema("catalog")` still creates the `catalog` schema *inside* `catalog_db`.

### 1.2 Connection strings: one field changes

| Service | Old `Database=` | New `Database=` | Login (unchanged) |
|---|---|---|---|
| Identity | `lankamart` | `identity_db` | `identity_svc` |
| Users | `lankamart` | `users_db` | `users_svc` |
| Catalog | `lankamart` | `catalog_db` | `catalog_svc` |
| Inventory | `lankamart` | `inventory_db` | `inventory_svc` |
| Orders | `lankamart` | `orders_db` | `orders_svc` |
| Payments | `lankamart` | `payments_db` | `payments_svc` |
| Notifications *(new, Sprint 3)* | — | `notifications_db` | `notifications_svc` |

Update every `appsettings.Development.json`, `docker-compose.apps.yml`, and the test fixtures if they hard-code `Database=lankamart` (Testcontainers tests connect as the container superuser to a default DB — they keep working).

### 1.3 Migration strategy: app starts ≠ schema changes

🧠 **Problem:** with 3 replicas of Orders starting at once, 3 processes race to run `Migrate()`. In production you want migrations to run **once, before** the new version receives traffic, by something that can fail loudly and stop the rollout.

**Solution:** add a "migrate and exit" mode to every service. In `BuildingBlocks/Data/MigrationExtensions.cs` add:

```csharp
/// <summary>Call right after Build(). Returns true when the process should EXIT (job mode).
///   • Development: auto-migrates (convenient)   • any env: `--migrate-only` migrates then exits   • elsewhere: never migrates at start (Database:AutoMigrate=false)</summary>
public static async Task<bool> RunMigrationsIfRequestedAsync<TContext>(this WebApplication app, string[] args) where TContext : DbContext
{
    var migrateOnly = args.Contains("--migrate-only");
    var auto = app.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment());
    if (migrateOnly || auto) await app.MigrateDatabaseAsync<TContext>();
    return migrateOnly;
}
```
In **each service's `Program.cs`** replace `await app.MigrateDatabaseAsync<XDb>();` with:
```csharp
if (await app.RunMigrationsIfRequestedAsync<XDb>(args)) return;   // `dotnet X.dll --migrate-only` = run migrations, then exit with code 0
```
*(For Identity keep the seeder call **after** this line; `IdentitySeeder` is idempotent and only creates roles outside Development.)*

The Dockerfile `ENTRYPOINT` already forwards arguments (`"$@"`). If you built your Dockerfile from an older copy of the Phase 0 guide, change it to `ENTRYPOINT ["sh", "-c", "exec dotnet $APP_DLL \"$@\"", "--"]` and put every `#` comment on its **own line** (a trailing `# …` after an instruction breaks the build).

### 1.4 Reset, re-seed, verify

```bash
docker compose down -v                                   # ⚠ wipes dev data — fine, it's seed data
docker compose up -d
docker exec -it lm-postgres psql -U postgres -c "\l"     # lists identity_db … payments_db (+ postgres/template*)
python scripts/dev.py                                    # services auto-migrate in Development
python scripts/seed_demo.py                              # rebuild demo data
```

✅ **Check**
1. `\l` shows one database per service, owners `<svc>_svc`.
2. **Isolation, stronger than before:** `docker exec -it lm-postgres psql -U catalog_svc -d orders_db -c "select 1"` → `FATAL: permission denied for database "orders_db"`. The login can't even *connect* to a neighbour.
3. Job mode: `dotnet run --project src/services/orders/LankaMart.Orders.Api --no-launch-profile -- --migrate-only` (set the env vars as in `dev.py`) → logs *"Database is up to date"* and **exits** with code 0; with the DB stopped it retries 10× then fails non-zero.
4. The whole Phase 1 gate flow still passes (register → buy → paid). `dotnet test` green.
5. *(Optional)* **Moving existing data** instead of reseeding: `pg_dump -U postgres -d lankamart -n orders --no-owner | psql -U postgres -d orders_db` then `ALTER SCHEMA orders OWNER TO orders_svc;` and reassign table ownership (`REASSIGN OWNED BY postgres TO orders_svc`). For seed data, reseeding is simpler and cleaner.

⚠️ **Pitfalls:** the init script doesn't run on an existing volume (`down -v` first) · Windows editors saving the `.sh` with CRLF endings → `bad interpreter` (the `.gitattributes` from Phase 0 prevents it) · forgetting to remove the Phase 1 `GRANT CREATE ON DATABASE` line from any custom SQL you kept · a service that keeps the old `Database=lankamart` → *"database does not exist"*.

Commit: `feat(db): database per service and migrate-only mode`. Add **ADR-017** (*DB-per-service via init script; migrations as a job*).

---

# PART D — SPRINT 2: TRANSACTIONAL OUTBOX + INBOX (Week 12)

🎯 **Goal:** no event is ever lost, and no event is ever applied twice — even if a service crashes at the worst moment or RabbitMQ is down for an hour. Retire every "save, *then* publish" pair from Phase 1.

🧠 **Why this is "the most important infrastructure decision"** (architecture doc §5.2): without it you have a demo; with it you have a system that survives production. Re-read Part B.1–B.2 now. Today's Phase 1 gaps this closes: *user saved but profile never created*, *payment recorded but order never marked paid*, *order cancelled but stock never released* — all caused by a crash between two writes.

### 2.1 Packages and the new messaging extension

```bash
cd src/shared/LankaMart.BuildingBlocks
dotnet add package MassTransit.EntityFrameworkCore --version "8.*"     # MUST stay v8 (v9 is commercial)
cd ../../..
```

Replace `BuildingBlocks/Messaging/MessagingExtensions.cs` with the **generic** version. Making it generic over the service's `DbContext` means *you cannot register messaging without choosing an outbox database* — forgetting becomes a compile error (that is how we "retire direct publishes").

```csharp
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LankaMart.BuildingBlocks;

public static class MessagingExtensions
{
    public static IServiceCollection AddPlatformMessaging<TDbContext>(this IServiceCollection services, IConfiguration config,
        string serviceName, Action<IBusRegistrationConfigurator>? configure = null) where TDbContext : DbContext
    {
        services.AddMassTransit(x =>
        {
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, includeNamespace: false));
            configure?.Invoke(x);                                  // consumers

            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UsePostgres();                                    // Postgres locking → several replicas can share one outbox safely
                o.UseBusOutbox();                                   // IPublishEndpoint.Publish now WRITES AN OUTBOX ROW when you call SaveChanges
                o.QueryDelay = TimeSpan.FromSeconds(1);             // how often the delivery service looks for undelivered rows
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);   // how long the INBOX remembers processed message ids
            });
            // INBOX + consumer outbox: every consumer runs inside a DB transaction together with its own changes,
            // the processed-message record, and any events it publishes → exactly-once EFFECT.
            x.AddConfigureEndpointsCallback((context, name, cfg) => cfg.UseEntityFrameworkOutbox<TDbContext>(context));

            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(config["RabbitMq:Host"] ?? "localhost", ushort.Parse(config["RabbitMq:Port"] ?? "5672"), "/", h =>
                {
                    h.Username(config["RabbitMq:Username"] ?? "lankamart");
                    h.Password(config["RabbitMq:Password"] ?? "lankamart_dev_pw");
                });
                cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
                cfg.ConfigureEndpoints(ctx);
            });
        });
        return services;
    }
}
```

And one more helper — `BuildingBlocks/Data/TransactionExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
namespace LankaMart.BuildingBlocks;

public static class TransactionExtensions
{
    /// <summary>Runs <paramref name="work"/> inside ONE database transaction, then SaveChanges (which also writes outbox rows) and commits.
    /// Use it whenever your code mixes ExecuteUpdate/ExecuteDelete (they run immediately) with Publish — without a transaction they
    /// would commit separately from the outbox row, recreating the dual-write problem.</summary>
    public static async Task<T> InTransactionAsync<T>(this DbContext db, Func<Task<T>> work, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)               // already inside one (e.g. a consumer: MassTransit opened it)
        { var inner = await work(); await db.SaveChangesAsync(ct); return inner; }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await work();                                    // an exception here → tx disposed without commit → ROLLBACK
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }
    public static Task InTransactionAsync(this DbContext db, Func<Task> work, CancellationToken ct = default) =>
        db.InTransactionAsync<bool>(async () => { await work(); return true; }, ct);
}
```

### 2.2 Add the outbox tables to every service

1. In every service's `DbContext.OnModelCreating` (after `base.OnModelCreating` for Identity) add:
```csharp
b.AddInboxStateEntity();
b.AddOutboxMessageEntity();
b.AddOutboxStateEntity();
```
2. In every `Program.cs` change `AddPlatformMessaging(cfg, "x", …)` → `AddPlatformMessaging<XDb>(cfg, "x", …)`.
3. Create the migration per service: `dotnet ef migrations add AddOutbox --project src/services/<svc>/LankaMart.<Svc>.Api --output-dir Data/Migrations`. You'll see three new tables per database: `outbox_message` (events waiting to be sent), `outbox_state` (delivery bookkeeping), `inbox_state` (processed message ids).

### 2.3 Change the code — the one rule

> **THE RULE.** A `Publish(...)` is only durable if a `SaveChanges` follows it **on the same `DbContext`, in the same transaction as the business change.** Publishing without a later `SaveChanges` silently drops the message.

Two situations:

**(a) All your changes go through tracked entities + one `SaveChangesAsync`** → just **move the `Publish` *before* the final `SaveChangesAsync`**. One `SaveChanges` is one transaction: entity changes and outbox row commit together.
**(b) You use `ExecuteUpdateAsync`/`ExecuteDelete` or several `SaveChanges`** (they commit immediately) → wrap in `db.InTransactionAsync(...)`.
**(c) Inside a consumer** → nothing to do: the consumer outbox already wraps the handler in a transaction and flushes publishes on success. (Throwing an exception rolls back *both* the handler's DB changes and its publishes — exactly what you want.)

**Where each Phase 1 `Publish` lives and what to change**

| Service | Publish site | Fix |
|---|---|---|
| Identity | `Register`, `VerifyContact`, login failure | **(b)** `InTransactionAsync` (UserManager saves internally) |
| Users | KYC submit, approve, reject, suspend, reinstate | **(a)** move `Publish` above `SaveChangesAsync` |
| Catalog | create product, add variant, publish, archive | **(a)** |
| Inventory | `PublishLowStockAsync`; `ConfirmAsync` failure event | low stock: publish then `SaveChangesAsync` · confirm: runs in a consumer **(c)** |
| Orders | checkout (`OrderCreated`), `CompensateAsync`, cancel endpoint, expiry worker, dev simulator | **(a)/(b)** see below · consumers **(c)** |
| Payments | `WebhookProcessor` | **(a)** |

**Identity — registration** (`AuthService.RegisterAsync`): the user row, role and event now commit atomically; the OTPs are sent **after** commit (Redis and SMS are not part of the DB transaction, and a user who exists but didn't get a code can simply press *resend*).
```csharp
await db.InTransactionAsync(async () =>
{
    var created = await users.CreateAsync(user, r.Password);
    if (!created.Succeeded) throw new BusinessRuleException("WEAK_PASSWORD", string.Join(" ", created.Errors.Select(e => e.Description)));
    await users.AddToRoleAsync(user, r.Role);
    await bus.Publish(new UserRegistered(user.Id, email, phone, user.FullName, r.Role, r.Language, clock.GetUtcNow()), ct);   // → outbox row, same transaction
}, ct);

await otp.SendAsync(user.Id, "email", email);       // after commit
await otp.SendAsync(user.Id, "phone", phone);
```
Do the same for `VerifyContactAsync` (`users.UpdateAsync` + publish) and for the failed-login path (`AccessFailedAsync` + `UserLoginFailed` inside the transaction; **throw `UnauthorizedException` after** the transaction commits so the failure counter isn't rolled back).

**Users / Catalog / Payments — move the publish up one line** (example: approve seller; same shape everywhere):
```csharp
s.Approve(c.User.UserId(), now);
await bus.Publish(new SellerApproved(s.UserId, s.BusinessName ?? s.DisplayName, now), ct);   // was AFTER SaveChanges
await db.SaveChangesAsync(ct);                                                               // ONE transaction: state + outbox row
```
Payments' webhook: `if (toPublish is not null) await bus.Publish(toPublish, ct);` goes **before** the `try { await db.SaveChangesAsync(ct); }` — and if the unique-violation catch fires, the outbox row rolls back together with the duplicate (perfect).

**Inventory — low stock** (`ReservationService.ReserveAsync`, end of method):
```csharp
await PublishLowStockAsync(ids, ct);      // now only calls bus.Publish(...)
await db.SaveChangesAsync(ct);            // flushes the outbox rows (no tracked entity changes needed)
```

**Orders — the careful one.** `OrderTransitions.TryAsync` already commits an `ExecuteUpdate`; wrap each "transition + publish" in a transaction:
```csharp
// CheckoutService.CompensateAsync
await db.InTransactionAsync(async () =>
{
    if (await OrderTransitions.TryAsync(db, order.Id, OrderStatus.PendingPayment, to, "system", reason, clock.GetUtcNow(), reason))
        await bus.Publish(new OrderCancelled(order.Id, order.CustomerId, reason, ToLines(order), clock.GetUtcNow()));
});

// ExpireStaleOrdersWorker — inside the foreach
await db.InTransactionAsync(async () =>
{
    if (await OrderTransitions.TryAsync(db, o.Id, OrderStatus.PendingPayment, OrderStatus.Expired, "system", "Payment window elapsed", now, "PAYMENT_TIMEOUT"))
        await bus.Publish(new OrderCancelled(o.Id, o.CustomerId, "PAYMENT_TIMEOUT", CheckoutService.ToLines(o), now));
}, stop);

// Checkout step 5/6: publish OrderCreated BEFORE the SaveChanges that stores the payment id → atomic
order.PaymentId = intent.PaymentId;
await bus.Publish(new OrderCreated(order.Id, order.OrderNumber, userId, order.TotalCents, now), ct);
await db.SaveChangesAsync(ct);
await carts.ClearAsync(cartKey);                     // Redis cleanup after the durable commit
return new CheckoutResult(...);
```
Apply the same wrapper to the **cancel endpoint** and the **dev simulator**. The consumers (`PaymentCompletedConsumer`, `PaymentFailedConsumer`, `ReservationConfirmFailedConsumer`) need **no code change** — they now run under the consumer outbox. (`OrderTransitions.TryAsync` calling `SaveChangesAsync` inside a consumer is fine; it's within MassTransit's transaction.)

### 2.4 Prove it

✅ **Check — these tests are the whole point of the sprint**
1. `dotnet build` (the generic extension forces every service to name its DbContext) and `dotnet ef database update`/restart → three new tables per DB. Register a user → `SELECT * FROM identity.outbox_message;` shows the row for ~1 s, then it disappears (delivered); the profile appears in Users.
2. **Broker-down test.** `docker compose stop rabbitmq`. Register a user, place an order, approve a seller — **every API call still succeeds** (in Phase 1 they'd have failed or lost events). `SELECT count(*) FROM <schema>.outbox_message` grows. `docker compose start rabbitmq` → counts drain to 0 within seconds and every downstream effect happens (profile created, order paid…). *You just survived a broker outage with zero data loss.*
3. **Consumer-down test.** Stop Users, register 5 customers, start Users → 5 profiles, no duplicates.
4. **Duplicate-delivery test (automated).** In a test using real Postgres (Testcontainers) publish the *same message id* twice to a consumer and assert the side effect happened once (`inbox_state` has one row; one `OrderPaid` produced):
   ```csharp
   await bus.Publish(msg, x => x.MessageId = fixedId);  await bus.Publish(msg, x => x.MessageId = fixedId);
   ```
5. **Atomicity test.** Make the handler throw *after* publishing (temporary `throw` at the end of an endpoint inside `InTransactionAsync`) → no state change **and** no outbox row. Remove the throw.
6. **Lag check** (use it in alerts later): `SELECT now() - min(enqueue_time) FROM orders.outbox_message;` should be ≈ 0; > 30 s means the delivery service or broker is stuck.
7. The full Phase 1 gate flow + `dotnet test` still pass.
8. In RabbitMQ UI check there are **no unexpected `_error` queue messages** after the above.

⚠️ **Pitfalls**
* **Forgot the `SaveChanges`** after `Publish` → the event silently vanishes. Add a test per publish site (assert an outbox row or the harness's published message).
* **Publishing from a `BackgroundService`** without creating a scope → resolve `IPublishEndpoint` and the `DbContext` from the *same* scope.
* **Two different `DbContext`s** in one request: the outbox row belongs to the context that `SaveChanges`. Keep one context per service.
* `40001` *serialization failure* under heavy load: the consumer outbox uses strict isolation; MassTransit retries those. If they become frequent, read MassTransit's outbox docs about isolation level and lock statements before changing anything.
* Don't hand-edit the outbox tables. Don't truncate them in production. They're evidence.

Update the **known-gaps register**: remove gap #1 ("publish-after-commit"); add: *"outbox delivery lag is only checked by hand (alerts come with Phase 6 monitoring)."*
Commit: `feat(messaging): transactional outbox and inbox in all services`. **ADR-018** (*Outbox via MassTransit EF, generic registration enforces it*).

---

# PART E — SPRINT 3: NOTIFICATION SERVICE (Weeks 13–14)

**Port 5107 · database `notifications_db` · login `notifications_svc` · schema `notifications`.**
🎯 **Goal:** every outbound message goes through **one** service: real emails (caught by Mailpit locally), an in-app inbox, SMS/push behind adapters, per-user preferences, retries with back-off, and a dead-letter queue. OTP codes stop being *logged* and start being *sent*.

🧠 **Why centralise** (architecture doc §4.9): if six services each send their own emails you get six broken retry stories and no unsubscribe management. One service = one queue, one retry policy, one place to add a channel. **Anti-pattern avoided: services never call SMS/email APIs directly. Ever.**

**Design in one picture**
```
 events (OrderPaid, SellerApproved, …) ──► consumers ──► NotificationService.Enqueue ──► notification_messages (Pending) + inbox_items
                                                                                              │
 DeliveryWorker (every 5 s): CLAIM batch (FOR UPDATE SKIP LOCKED + 2-min lease) ──► render template ──► Email (SMTP) / SMS / Push adapter
        success → Sent      failure → back-off 1m → 5m → 30m → 2h → 12h → DeadLetter (visible to admins, can be requeued)

 Identity ──(internal REST, synchronous)──► POST /internal/notifications/otp ──► sent immediately, code NEVER stored
```
**Two deliberate design choices**
* **OTP is a synchronous internal call, not an event.** A code on the event bus would sit in RabbitMQ, logs and outbox tables — secrets don't belong there. The user is also *waiting* for it. Everything else is asynchronous events.
* **Contacts are a local read-model** built from `UserRegistered` (email, phone, language). Notification never calls Users/Identity to look people up — same pattern as Catalog's `known_sellers`.

**Channel policy (System Scenarios §7 / architecture doc §4.9):** *Email* — everything transactional · *SMS* — **only** OTP/security (it costs money) · *Push* — order status, promos (free) · *In-app* — everything, permanent record · *WhatsApp* — v2.

### 3.1 Create the project

```bash
dotnet new web -n LankaMart.Notifications.Api -o src/services/notifications/LankaMart.Notifications.Api
dotnet sln add src/services/notifications/LankaMart.Notifications.Api
dotnet add src/services/notifications/LankaMart.Notifications.Api reference src/shared/LankaMart.BuildingBlocks src/shared/LankaMart.Contracts
cd src/services/notifications/LankaMart.Notifications.Api
dotnet add package Microsoft.EntityFrameworkCore.Design --version "10.*"
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version "10.*"
dotnet add package EFCore.NamingConventions --version "10.*"
dotnet add package MailKit
cd ../../../..
```
Add `GlobalUsings.cs` (§1.5 of the Phase 1 guide), port **5107** in `scripts/dev.py`, a `notifications` cluster in the gateway config (`http://localhost:5107/`) and two gateway routes — `/api/v1/me/{**rest}` and `/api/v1/admin/notifications/{**rest}` → cluster `notifications` (both **require** a token, so no `AuthorizationPolicy` line). Config (`appsettings.Development.json`) = the standard template with `Database=notifications_db;Username=notifications_svc;Password=notifications_dev_pw`, plus:
```json
"Email": { "Host": "localhost", "Port": 1025, "UseTls": false, "User": "", "Password": "", "From": "LankaMart <no-reply@lankamart.local>" },
"Sms":   { "Provider": "log", "Url": "", "ApiKey": "" },
"Push":  { "Provider": "log" },
"Delivery": { "PollSeconds": 5, "BatchSize": 20, "MaxAttempts": 5 }
```
Add **Mailpit** (a local fake SMTP server with a web inbox) to `docker-compose.yml`:
```yaml
  mailpit:
    image: axllent/mailpit:latest
    container_name: lm-mailpit
    restart: unless-stopped
    ports:
      - "1025:1025"     # SMTP — the Notification service sends here
      - "8025:8025"     # web inbox — http://localhost:8025
```

### 3.2 Domain & database

`Domain/Entities.cs`
```csharp
namespace LankaMart.Notifications.Api.Domain;

public enum Channel { Email, Sms, Push, InApp }
public enum DeliveryStatus { Pending, Sending, Sent, Failed, DeadLetter, Skipped }

/// <summary>Local read-model: who to contact and in which language. Built from UserRegistered.</summary>
public class Contact { public Guid UserId { get; set; } public string Email { get; set; } = ""; public string Phone { get; set; } = ""; public string Language { get; set; } = "en"; public DateTimeOffset UpdatedAt { get; set; } }

public class NotificationMessage
{
    public Guid Id { get; set; } public Guid UserId { get; set; }
    public Channel Channel { get; set; } public string TemplateKey { get; set; } = ""; public string Category { get; set; } = "system";
    public string DataJson { get; set; } = "{}";                       // placeholder values; the text is rendered at SEND time so retries use fixed templates
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int Attempts { get; set; } public DateTimeOffset NextAttemptAt { get; set; } public DateTimeOffset? LockedUntil { get; set; }   // lease: a crashed worker's claim expires
    public string? LastError { get; set; } public string? DedupeKey { get; set; }                                                         // unique → the same event can't notify twice
    public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset? SentAt { get; set; }
}

public class InboxItem { public Guid Id { get; set; } public Guid UserId { get; set; } public string Title { get; set; } = ""; public string Body { get; set; } = ""; public string? DeepLink { get; set; } public string Category { get; set; } = "system"; public DateTimeOffset? ReadAt { get; set; } public DateTimeOffset CreatedAt { get; set; } }

public class Preference
{
    public Guid UserId { get; set; } public string Category { get; set; } = ""; public bool Email { get; set; } public bool Sms { get; set; } public bool Push { get; set; } public bool InApp { get; set; }
    public bool Allows(Channel c) => c switch { Channel.Email => Email, Channel.Sms => Sms, Channel.Push => Push, _ => InApp };
}
public static class PreferenceDefaults
{
    public static readonly string[] UserEditable = ["orders", "promos", "system"];     // "security" (OTP, password changes) can NEVER be switched off
    public static Preference For(Guid userId, string category) => category switch
    {
        "security" => new() { UserId = userId, Category = category, Email = true, Sms = true, Push = true, InApp = true },
        "promos"   => new() { UserId = userId, Category = category, Email = false, Sms = false, Push = false, InApp = true },   // marketing is opt-IN
        _          => new() { UserId = userId, Category = category, Email = true, Sms = false, Push = true, InApp = true }      // SMS stays off: it costs money
    };
}

public class DeviceToken { public Guid Id { get; set; } public Guid UserId { get; set; } public string Token { get; set; } = ""; public string Platform { get; set; } = "android"; public DateTimeOffset CreatedAt { get; set; } }
public class Template { public string Key { get; set; } = ""; public string Lang { get; set; } = "en"; public Channel Channel { get; set; } public string? Subject { get; set; } public string Body { get; set; } = ""; }
```

`Data/NotificationsDb.cs`
```csharp
using LankaMart.Notifications.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace LankaMart.Notifications.Api.Data;

public class NotificationsDb(DbContextOptions<NotificationsDb> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<NotificationMessage> Messages => Set<NotificationMessage>();
    public DbSet<InboxItem> Inbox => Set<InboxItem>();
    public DbSet<Preference> Preferences => Set<Preference>();
    public DbSet<DeviceToken> Devices => Set<DeviceToken>();
    public DbSet<Template> Templates => Set<Template>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("notifications");
        b.Entity<Contact>().HasKey(x => x.UserId);
        b.Entity<NotificationMessage>(e =>
        {
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.HasIndex(x => x.DedupeKey).IsUnique().HasFilter("dedupe_key IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });
        b.Entity<InboxItem>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<Preference>().HasKey(x => new { x.UserId, x.Category });
        b.Entity<DeviceToken>().HasIndex(x => x.Token).IsUnique();
        b.Entity<Template>(e => { e.HasKey(x => new { x.Key, x.Lang, x.Channel }); e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10); });
        b.AddInboxStateEntity(); b.AddOutboxMessageEntity(); b.AddOutboxStateEntity();
    }
}
```
`dotnet ef migrations add Initial --project src/services/notifications/LankaMart.Notifications.Api --output-dir Data/Migrations`

### 3.3 Templates and the enqueue logic

🧠 *Templates are data, not code.* Product admins will edit wording; Phase 3 moves them into the **Localization service** (so Sinhala/Tamil arrive with no changes here). Rendering is plain `{placeholder}` replacement — **no scripting language, so no template-injection risk** — and values are HTML-encoded in emails (a customer named `<script>` can't inject markup).

`Features/Templates.cs`
```csharp
using System.Text.RegularExpressions;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Notifications.Api.Features;

public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{(\w+)\}")] private static partial Regex Placeholder();
    public static string Render(string template, IReadOnlyDictionary<string, string> data, bool html) =>
        Placeholder().Replace(template, m => { var v = data.GetValueOrDefault(m.Groups[1].Value, ""); return html ? System.Net.WebUtility.HtmlEncode(v) : v; });
}

public static class TemplateSeeder
{
    // (key, channel, subject, body)   English now; si/ta rows arrive with the Localization service in Phase 3
    private static readonly (string, Channel, string?, string)[] Seed =
    [
        ("auth.otp", Channel.Sms, null, "LankaMart: your verification code is {code}. It expires in 5 minutes. Never share it."),
        ("auth.otp", Channel.Email, "Your LankaMart verification code", "<p>Your verification code is <b>{code}</b>.</p><p>It expires in 5 minutes. If you didn't request it, ignore this email.</p>"),
        ("user.welcome", Channel.Email, "Welcome to LankaMart, {name}", "<p>Hi {name}, welcome to LankaMart! Start browsing anytime.</p>"),
        ("user.welcome", Channel.InApp, "Welcome, {name}!", "Your account is ready. Happy shopping."),
        ("order.paid", Channel.Email, "Order {orderNumber} confirmed", "<p>Thanks for your order <b>{orderNumber}</b>. We received your payment of <b>{total}</b>.</p>"),
        ("order.paid", Channel.InApp, "Order {orderNumber} confirmed", "Payment of {total} received. We'll keep you posted."),
        ("order.paid", Channel.Push, "Order confirmed", "Order {orderNumber} is confirmed."),
        ("order.status", Channel.InApp, "Order update", "{text}"),
        ("order.status", Channel.Push, "Order update", "{text}"),
        ("order.cancelled", Channel.InApp, "Order cancelled", "Your order was cancelled ({reason})."),
        ("payment.failed", Channel.InApp, "Payment failed", "Your payment didn't go through ({reason}). Your cart was restored — you can try again."),
        ("seller.approved", Channel.Email, "Your seller account is approved", "<p>Congratulations {name}! You can now list products on LankaMart.</p>"),
        ("seller.approved", Channel.InApp, "Seller account approved", "You can now list products."),
        ("seller.rejected", Channel.Email, "Your seller application needs changes", "<p>Hi {name}, your application was not approved (reason: {reason}). You can fix the details and resubmit.</p>"),
        ("seller.rejected", Channel.InApp, "Seller application needs changes", "Reason: {reason}. You can resubmit."),
        ("stock.low", Channel.InApp, "Low stock", "SKU {sku} is running low ({available} left)."),
    ];

    public static async Task SeedAsync(NotificationsDb db)
    {
        foreach (var (key, ch, subject, body) in Seed)
            if (!await db.Templates.AnyAsync(t => t.Key == key && t.Lang == "en" && t.Channel == ch))
                db.Templates.Add(new Template { Key = key, Lang = "en", Channel = ch, Subject = subject, Body = body });
        await db.SaveChangesAsync();
    }
}
```

`Features/NotificationService.cs`
```csharp
using System.Text.Json;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Notifications.Api.Features;

public sealed class NotificationService(NotificationsDb db, TimeProvider clock, ILogger<NotificationService> log)
{
    public async Task<Template?> FindTemplateAsync(string key, string lang, Channel ch, CancellationToken ct) =>
        await db.Templates.AsNoTracking().Where(t => t.Key == key && t.Channel == ch && (t.Lang == lang || t.Lang == "en"))
                .OrderBy(t => t.Lang == lang ? 0 : 1).FirstOrDefaultAsync(ct);               // fallback chain: user's language → English

    /// <summary>Decide WHICH channels this user wants for this category, create the inbox item immediately and queue the rest for the delivery worker.
    /// Idempotent per <paramref name="dedupe"/> (typically the event's message id).</summary>
    public async Task EnqueueAsync(Guid userId, string category, string templateKey, Dictionary<string, string> data, IEnumerable<Channel> channels,
                                   string? dedupe, string? deepLink = null, CancellationToken ct = default)
    {
        var contact = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, ct);
        if (contact is null) { log.LogWarning("No contact for {UserId}; skipping {Template}", userId, templateKey); return; }   // profile event not processed yet / admin account
        var pref = await db.Preferences.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId && p.Category == category, ct) ?? PreferenceDefaults.For(userId, category);
        var now = clock.GetUtcNow();

        foreach (var ch in channels.Distinct())
        {
            if (!pref.Allows(ch)) continue;                                                   // respect the user's choice (System Scenarios §7)
            var tpl = await FindTemplateAsync(templateKey, contact.Language, ch, ct);
            if (tpl is null) continue;
            var key = dedupe is null ? null : $"{dedupe}:{ch}";
            if (key is not null && await db.Messages.AnyAsync(m => m.DedupeKey == key, ct)) continue;

            var msg = new NotificationMessage { Id = Guid.CreateVersion7(), UserId = userId, Channel = ch, TemplateKey = templateKey, Category = category,
                                                DataJson = JsonSerializer.Serialize(data), CreatedAt = now, NextAttemptAt = now, DedupeKey = key };
            if (ch == Channel.InApp)
            {
                db.Inbox.Add(new InboxItem { Id = Guid.CreateVersion7(), UserId = userId, Category = category, DeepLink = deepLink, CreatedAt = now,
                    Title = TemplateRenderer.Render(tpl.Subject ?? "", data, html: false), Body = TemplateRenderer.Render(tpl.Body, data, html: false) });
                msg.Status = DeliveryStatus.Sent; msg.SentAt = now;                           // in-app needs no delivery step; row kept for audit/dedupe
            }
            db.Messages.Add(msg);
        }
        await db.SaveChangesAsync(ct);
    }
}
```

### 3.4 Channel adapters

`Delivery/Senders.cs`
```csharp
using System.Net.Http.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LankaMart.Notifications.Api.Delivery;

public interface IEmailSender { Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct); }
public interface ISmsSender   { Task SendAsync(string toE164, string text, CancellationToken ct); }
public interface IPushSender  { Task SendAsync(string deviceToken, string title, string body, CancellationToken ct); }

public sealed class EmailOptions { public string Host { get; set; } = "localhost"; public int Port { get; set; } = 1025; public bool UseTls { get; set; } public string User { get; set; } = ""; public string Password { get; set; } = ""; public string From { get; set; } = ""; }

/// <summary>Plain SMTP. Mailpit locally; in production point it at SendGrid's or Amazon SES's SMTP relay — no vendor SDK, no code change.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> opt) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        var o = opt.Value; var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(o.From)); msg.To.Add(MailboxAddress.Parse(to)); msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();
        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(o.Host, o.Port, o.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(o.User)) await smtp.AuthenticateAsync(o.User, o.Password, ct);
        await smtp.SendAsync(msg, ct); await smtp.DisconnectAsync(true, ct);
    }
}

/// <summary>Dev SMS: logs the message (the phone number is masked). Never log full OTP SMS bodies in production.</summary>
public sealed class LogSmsSender(ILogger<LogSmsSender> log) : ISmsSender
{
    public Task SendAsync(string to, string text, CancellationToken ct) { log.LogWarning("[DEV SMS] to {To}: {Text}", to[..Math.Min(6, to.Length)] + "****", text); return Task.CompletedTask; }
}

/// <summary>Generic REST SMS aggregator adapter. Adjust the JSON shape to your provider's API (Dialog/Mobitel/others publish docs).
/// If a provider only offers SOAP, write the same interface with a generated client — see Appendix F of the Phase 0–1 guide.</summary>
public sealed class HttpSmsSender(IHttpClientFactory f, IConfiguration cfg) : ISmsSender
{
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        var http = f.CreateClient("sms");
        using var req = new HttpRequestMessage(HttpMethod.Post, cfg["Sms:Url"]) { Content = JsonContent.Create(new { to, message = text }) };
        req.Headers.Authorization = new("Bearer", cfg["Sms:ApiKey"]);
        (await http.SendAsync(req, ct)).EnsureSuccessStatusCode();
    }
}

public sealed class LogPushSender(ILogger<LogPushSender> log) : IPushSender
{
    public Task SendAsync(string token, string title, string body, CancellationToken ct) { log.LogInformation("[DEV PUSH] {Title}: {Body}", title, body); return Task.CompletedTask; }
}
```
*(Real push with Firebase Cloud Messaging: `dotnet add package FirebaseAdmin`, create a Firebase project and service-account JSON (kept in a secret!), `FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromFile(path) })`, then `FirebaseMessaging.DefaultInstance.SendAsync(new Message { Token = t, Notification = new Notification { Title = …, Body = … } })`. Implement it as `FcmPushSender : IPushSender` and select it with `Push:Provider=fcm`. Handle `FirebaseMessagingException` with `MessagingErrorCode.Unregistered` by deleting that stale token. Do it when you build the Flutter push feature — Phase 7.)*

### 3.5 The delivery worker — claim, send, back off

🧠 **The claim pattern.** Several replicas may run this worker. `SELECT … FOR UPDATE SKIP LOCKED` lets each replica lock a *different* batch of rows instantly (rows locked by others are skipped, not waited for). We mark the batch `Sending` with a **2-minute lease** and commit, then do the slow network calls **outside** the transaction. If a worker dies mid-send, its lease expires and another worker picks the rows up. *Competing consumers without a coordinator.*

`Delivery/DeliveryWorker.cs`
```csharp
using System.Text.Json;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Domain;
using LankaMart.Notifications.Api.Features;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Notifications.Api.Delivery;

public sealed class DeliveryWorker(IServiceScopeFactory scopes, IConfiguration cfg, TimeProvider clock, ILogger<DeliveryWorker> log) : BackgroundService
{
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)];

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(cfg.GetValue("Delivery:PollSeconds", 5)));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try { while (await RunBatchAsync(stop) > 0) { } }                    // keep draining while there is work
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Delivery loop failed"); }
        }
    }

    private async Task<int> RunBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<NotificationsDb>();
        var batch = await ClaimAsync(db, ct);
        foreach (var m in batch)
        {
            var now = clock.GetUtcNow();
            try
            {
                var outcome = await SendOneAsync(sp, db, m, ct);
                await db.Messages.Where(x => x.Id == m.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, outcome).SetProperty(x => x.SentAt, now).SetProperty(x => x.LockedUntil, (DateTimeOffset?)null).SetProperty(x => x.LastError, (string?)null), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var attempts = m.Attempts + 1;
                var dead = attempts >= cfg.GetValue("Delivery:MaxAttempts", 5);
                var next = now + Backoff[Math.Min(attempts - 1, Backoff.Length - 1)];
                var err = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
                log.LogWarning("Notification {Id} ({Channel}) attempt {Attempts} failed: {Error}", m.Id, m.Channel, attempts, err);
                await db.Messages.Where(x => x.Id == m.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Attempts, attempts).SetProperty(x => x.LastError, err).SetProperty(x => x.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(x => x.Status, dead ? DeliveryStatus.DeadLetter : DeliveryStatus.Pending).SetProperty(x => x.NextAttemptAt, next), ct);
            }
        }
        return batch.Count;
    }

    private async Task<List<NotificationMessage>> ClaimAsync(NotificationsDb db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow(); var size = cfg.GetValue("Delivery:BatchSize", 20);
        var ids = await db.Messages.FromSqlRaw($"""
                SELECT * FROM notifications.notification_messages
                WHERE (status = 'Pending' AND next_attempt_at <= now()) OR (status = 'Sending' AND locked_until < now())
                ORDER BY next_attempt_at LIMIT {size} FOR UPDATE SKIP LOCKED
                """).Select(m => m.Id).ToListAsync(ct);
        if (ids.Count == 0) { await tx.CommitAsync(ct); return []; }
        await db.Messages.Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, DeliveryStatus.Sending).SetProperty(m => m.LockedUntil, now.AddMinutes(2)), ct);
        await tx.CommitAsync(ct);
        return await db.Messages.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(ct);
    }

    private static async Task<DeliveryStatus> SendOneAsync(IServiceProvider sp, NotificationsDb db, NotificationMessage m, CancellationToken ct)
    {
        var svc = sp.GetRequiredService<NotificationService>();
        var contact = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == m.UserId, ct) ?? throw new InvalidOperationException("Contact missing");
        var tpl = await svc.FindTemplateAsync(m.TemplateKey, contact.Language, m.Channel, ct) ?? throw new InvalidOperationException($"Template {m.TemplateKey}/{m.Channel} missing");
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(m.DataJson) ?? [];
        switch (m.Channel)
        {
            case Channel.Email:
                await sp.GetRequiredService<IEmailSender>().SendAsync(contact.Email, TemplateRenderer.Render(tpl.Subject ?? "", data, false), TemplateRenderer.Render(tpl.Body, data, true), ct); break;
            case Channel.Sms:
                await sp.GetRequiredService<ISmsSender>().SendAsync(contact.Phone, TemplateRenderer.Render(tpl.Body, data, false), ct); break;
            case Channel.Push:
                var tokens = await db.Devices.AsNoTracking().Where(d => d.UserId == m.UserId).Select(d => d.Token).ToListAsync(ct);
                if (tokens.Count == 0) return DeliveryStatus.Skipped;                                    // no registered device: not an error
                foreach (var t in tokens) await sp.GetRequiredService<IPushSender>().SendAsync(t, TemplateRenderer.Render(tpl.Subject ?? "", data, false), TemplateRenderer.Render(tpl.Body, data, false), ct); break;
        }
        return DeliveryStatus.Sent;
    }
}
```
*(`FromSqlRaw($"…")` with an interpolated `size` is safe here because `size` is an `int` from configuration, never user input; EF would parameterise `FromSql` interpolation but `FOR UPDATE … LIMIT` is easier to read this way. Never do this with strings from users.)*

### 3.6 Consumers — events become notifications

`Consumers/Consumers.cs`
```csharp
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Domain;
using LankaMart.Notifications.Api.Features;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Notifications.Api.Consumers;

static class N { public static string Lkr(long cents) => $"LKR {cents / 100m:N2}"; public static string? Id(ConsumeContext c) => c.MessageId?.ToString(); }

public sealed class UserRegisteredConsumer(NotificationsDb db, NotificationService n) : IConsumer<UserRegistered>
{
    public async Task Consume(ConsumeContext<UserRegistered> c)
    {
        var m = c.Message;
        var contact = await db.Contacts.SingleOrDefaultAsync(x => x.UserId == m.UserId);
        if (contact is null) db.Contacts.Add(new Contact { UserId = m.UserId, Email = m.Email, Phone = m.Phone, Language = m.Language, UpdatedAt = m.OccurredAt });
        else { contact.Email = m.Email; contact.Phone = m.Phone; contact.Language = m.Language; contact.UpdatedAt = m.OccurredAt; }
        await db.SaveChangesAsync();
        await n.EnqueueAsync(m.UserId, "system", "user.welcome", new() { ["name"] = m.FullName }, [Channel.Email, Channel.InApp], N.Id(c));
    }
}

public sealed class OrderConsumers(NotificationService n) : IConsumer<OrderPaid>, IConsumer<OrderStatusChanged>, IConsumer<OrderCancelled>
{
    private static readonly Dictionary<string, string> Text = new()
    {
        ["Processing"] = "The seller is preparing your order.", ["ReadyForPickup"] = "Your parcel is ready for courier pickup.", ["PickedUp"] = "Your parcel was picked up.",
        ["AtHub"] = "Your parcel arrived at our hub.", ["OutForDelivery"] = "Your parcel is out for delivery today.", ["Delivered"] = "Your parcel was delivered. Enjoy!"
    };
    public Task Consume(ConsumeContext<OrderPaid> c) => n.EnqueueAsync(c.Message.CustomerId, "orders", "order.paid",
        new() { ["orderNumber"] = c.Message.OrderNumber, ["total"] = N.Lkr(c.Message.TotalCents) }, [Channel.Email, Channel.InApp, Channel.Push], N.Id(c), $"/orders/{c.Message.OrderId}");
    public Task Consume(ConsumeContext<OrderStatusChanged> c) =>
        Text.TryGetValue(c.Message.To, out var t)
            ? n.EnqueueAsync(c.Message.CustomerId, "orders", "order.status", new() { ["text"] = t }, [Channel.InApp, Channel.Push], N.Id(c), $"/orders/{c.Message.OrderId}")
            : Task.CompletedTask;                                          // "Paid" is already covered by OrderPaid
    public Task Consume(ConsumeContext<OrderCancelled> c) => n.EnqueueAsync(c.Message.CustomerId, "orders", "order.cancelled",
        new() { ["reason"] = c.Message.Reason.Replace('_', ' ').ToLowerInvariant() }, [Channel.InApp], N.Id(c), $"/orders/{c.Message.OrderId}");
}

public sealed class PaymentFailedConsumer(NotificationService n) : IConsumer<PaymentFailed>
{ public Task Consume(ConsumeContext<PaymentFailed> c) => n.EnqueueAsync(c.Message.CustomerId, "orders", "payment.failed", new() { ["reason"] = c.Message.FailureCode }, [Channel.InApp], N.Id(c), $"/orders/{c.Message.OrderId}"); }

public sealed class SellerConsumers(NotificationsDb db, NotificationService n) : IConsumer<SellerApproved>, IConsumer<SellerRejected>, IConsumer<InventoryLowStock>
{
    public Task Consume(ConsumeContext<SellerApproved> c) => n.EnqueueAsync(c.Message.SellerId, "system", "seller.approved", new() { ["name"] = c.Message.BusinessName }, [Channel.Email, Channel.InApp], N.Id(c));
    public Task Consume(ConsumeContext<SellerRejected> c) => n.EnqueueAsync(c.Message.SellerId, "system", "seller.rejected", new() { ["name"] = "there", ["reason"] = c.Message.ReasonCode.Replace('_', ' ').ToLowerInvariant() }, [Channel.Email, Channel.InApp], N.Id(c));
    public Task Consume(ConsumeContext<InventoryLowStock> c) => n.EnqueueAsync(c.Message.SellerId, "system", "stock.low", new() { ["sku"] = c.Message.VariantId.ToString()[..8], ["available"] = c.Message.Available.ToString() }, [Channel.InApp], N.Id(c));
}
```
*(`SellerKycSubmitted` has no consumer on purpose: it should notify *staff*, which needs role-based recipients and the admin panel — Phase 3. Today the Seller Manager just opens the queue.)*

### 3.7 Endpoints, internal OTP API, `Program.cs`

`Features/Endpoints.cs`
```csharp
using FluentValidation;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Delivery;
using LankaMart.Notifications.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Notifications.Api.Features;

public sealed record PreferenceDto(string Category, bool Email, bool Sms, bool Push, bool InApp);
public sealed record DeviceRequest(string Token, string Platform);
public sealed record SendOtpRequest(string Channel, string Destination, string Code, string Language);
public sealed class DeviceValidator : AbstractValidator<DeviceRequest> { public DeviceValidator() { RuleFor(x => x.Token).NotEmpty().MaximumLength(500); RuleFor(x => x.Platform).Must(p => p is "android" or "ios" or "web"); } }
public sealed class PreferenceValidator : AbstractValidator<PreferenceDto> { public PreferenceValidator() { RuleFor(x => x.Category).Must(c => PreferenceDefaults.UserEditable.Contains(c)); } }   // "security" can't be edited

public static class Endpoints
{
    public static void MapNotifications(this WebApplication app)
    {
        var me = app.MapGroup("/api/v1/me").WithTags("Notifications").RequireAuthorization();

        me.MapGet("/notifications", async (HttpContext c, NotificationsDb db, bool? unread, int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = Paging.Normalize(page, pageSize); var uid = c.User.UserId();
            var q = db.Inbox.AsNoTracking().Where(i => i.UserId == uid);                       // always the caller's own inbox (IDOR-proof)
            if (unread == true) q = q.Where(i => i.ReadAt == null);
            var total = await q.LongCountAsync(ct);
            var items = await q.OrderByDescending(i => i.CreatedAt).Skip((p - 1) * size).Take(size).Select(i => new { i.Id, i.Title, i.Body, i.DeepLink, i.Category, read = i.ReadAt != null, i.CreatedAt }).ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(items.Cast<object>().ToList(), p, size, total));
        });
        me.MapGet("/notifications/unread-count", async (HttpContext c, NotificationsDb db, CancellationToken ct) =>
            Results.Ok(new { count = await db.Inbox.CountAsync(i => i.UserId == c.User.UserId() && i.ReadAt == null, ct) }));
        me.MapPost("/notifications/{id:guid}/read", async (Guid id, HttpContext c, NotificationsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var n = await db.Inbox.Where(i => i.Id == id && i.UserId == c.User.UserId() && i.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(i => i.ReadAt, clock.GetUtcNow()), ct);
            return Results.NoContent();                                                          // idempotent; someone else's id is simply a no-op
        });
        me.MapPost("/notifications/read-all", async (HttpContext c, NotificationsDb db, TimeProvider clock, CancellationToken ct) =>
        { await db.Inbox.Where(i => i.UserId == c.User.UserId() && i.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(i => i.ReadAt, clock.GetUtcNow()), ct); return Results.NoContent(); });

        me.MapGet("/notification-preferences", async (HttpContext c, NotificationsDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId(); var saved = await db.Preferences.AsNoTracking().Where(p => p.UserId == uid).ToListAsync(ct);
            return Results.Ok(PreferenceDefaults.UserEditable.Select(cat => saved.FirstOrDefault(p => p.Category == cat) ?? PreferenceDefaults.For(uid, cat))
                .Select(p => new PreferenceDto(p.Category, p.Email, p.Sms, p.Push, p.InApp)));
        });
        me.MapPut("/notification-preferences", async (PreferenceDto r, HttpContext c, NotificationsDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId(); var p = await db.Preferences.SingleOrDefaultAsync(x => x.UserId == uid && x.Category == r.Category, ct);
            if (p is null) db.Preferences.Add(p = new Preference { UserId = uid, Category = r.Category });
            p.Email = r.Email; p.Sms = r.Sms; p.Push = r.Push; p.InApp = r.InApp;
            await db.SaveChangesAsync(ct); return Results.NoContent();
        }).WithValidation<PreferenceDto>();

        me.MapPost("/devices", async (DeviceRequest r, HttpContext c, NotificationsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var uid = c.User.UserId(); var d = await db.Devices.SingleOrDefaultAsync(x => x.Token == r.Token, ct);
            if (d is null) db.Devices.Add(new DeviceToken { Id = Guid.CreateVersion7(), UserId = uid, Token = r.Token, Platform = r.Platform, CreatedAt = clock.GetUtcNow() });
            else d.UserId = uid;                                                                   // device changed hands (logout/login as someone else)
            await db.SaveChangesAsync(ct); return Results.NoContent();
        }).WithValidation<DeviceRequest>();

        // ADMIN: dead-letter queue (messages that failed 5 times)
        var admin = app.MapGroup("/api/v1/admin/notifications").WithTags("Admin: Notifications").RequireAuthorization(p => p.RequireRole(Roles.CustomerManager, Roles.SuperAdmin));
        admin.MapGet("/dead-letter", async (NotificationsDb db, CancellationToken ct) =>
            Results.Ok(await db.Messages.AsNoTracking().Where(m => m.Status == DeliveryStatus.DeadLetter).OrderByDescending(m => m.CreatedAt).Take(100)
                .Select(m => new { m.Id, m.UserId, channel = m.Channel.ToString(), m.TemplateKey, m.Attempts, m.LastError, m.CreatedAt }).ToListAsync(ct)));
        admin.MapPost("/{id:guid}/requeue", async (Guid id, NotificationsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var n = await db.Messages.Where(m => m.Id == id && m.Status == DeliveryStatus.DeadLetter)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, DeliveryStatus.Pending).SetProperty(m => m.Attempts, 0).SetProperty(m => m.NextAttemptAt, clock.GetUtcNow()), ct);
            return n == 0 ? throw new NotFoundException() : Results.NoContent();
        });

        // INTERNAL: OTP. Synchronous, immediate, and the code is NEVER written to the database.
        app.MapInternalGroup("/api/v1/internal/notifications").MapPost("/otp", async (SendOtpRequest r, NotificationService svc, IServiceProvider sp, NotificationsDb db, CancellationToken ct) =>
        {
            var ch = r.Channel == "email" ? Channel.Email : Channel.Sms;
            var tpl = await svc.FindTemplateAsync("auth.otp", Lang.Normalize(r.Language), ch, ct) ?? throw new NotFoundException("Template missing");
            var data = new Dictionary<string, string> { ["code"] = r.Code };
            if (ch == Channel.Email) await sp.GetRequiredService<IEmailSender>().SendAsync(r.Destination, TemplateRenderer.Render(tpl.Subject ?? "", data, false), TemplateRenderer.Render(tpl.Body, data, true), ct);
            else await sp.GetRequiredService<ISmsSender>().SendAsync(r.Destination, TemplateRenderer.Render(tpl.Body, data, false), ct);
            return Results.Accepted();                                                        // failure → exception → 5xx → Identity lets the user resend
        });
    }
}
```
`Program.cs`
```csharp
using FluentValidation;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Delivery;
using LankaMart.Notifications.Api.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("notifications");

builder.Services.AddDbContext<NotificationsDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "notifications")).UseSnakeCaseNamingConvention());
builder.Services.Configure<EmailOptions>(cfg.GetSection("Email"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
if (cfg["Sms:Provider"] == "http") builder.Services.AddScoped<ISmsSender, HttpSmsSender>(); else builder.Services.AddScoped<ISmsSender, LogSmsSender>();
builder.Services.AddScoped<IPushSender, LogPushSender>();
if (cfg.GetValue("Workers:Enabled", true)) builder.Services.AddHostedService<DeliveryWorker>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging<NotificationsDb>(cfg, "notifications", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
if (await app.RunMigrationsIfRequestedAsync<NotificationsDb>(args)) return;
using (var scope = app.Services.CreateScope()) await TemplateSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<NotificationsDb>());
app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapNotifications();
app.Run();

public partial class Program;
```
*(`TemplateSeeder` must run only when the app really serves — job mode returns before it. In a fresh Kubernetes cluster the first normal start runs after the migration job, so the tables exist.)*

### 3.8 Plug OTP delivery into Identity

In **Identity**: add a typed internal client and the notification sender, then switch by config.
```csharp
// Security/NotificationOtpSender.cs
public interface INotificationsClient { Task SendOtpAsync(string channel, string destination, string code, CancellationToken ct); }
public sealed class NotificationsClient(HttpClient http) : INotificationsClient
{
    public async Task SendOtpAsync(string channel, string destination, string code, CancellationToken ct) =>
        (await http.PostAsJsonAsync("/api/v1/internal/notifications/otp", new SendOtpRequest(channel, destination, code, "en"), ct)).EnsureSuccessStatusCode();
}
public sealed class NotificationOtpSender(INotificationsClient client) : IOtpSender
{ public Task SendAsync(string channel, string destination, string code) => client.SendOtpAsync(channel, destination, code, default); }
```
Contracts: add `public sealed record SendOtpRequest(string Channel, string Destination, string Code, string Language);` to `Contracts/InternalApi.cs`. In Identity `Program.cs`:
```csharp
if (cfg["Otp:Sender"] == "notification")
{
    builder.Services.AddInternalClient<INotificationsClient, NotificationsClient>(cfg, "Notifications");   // + "Services": { "Notifications": "http://localhost:5107" }
    builder.Services.AddScoped<IOtpSender, NotificationOtpSender>();
}
else builder.Services.AddScoped<IOtpSender, LogOtpSender>();
builder.Services.AddScoped<OtpService>();        // was Singleton — it now depends on a scoped sender
```
In `OtpService.SendAsync` make delivery failure **non-fatal** and un-block the resend:
```csharp
try { await sender.SendAsync(channel, destination, code); }
catch (Exception ex) { await Db.KeyDeleteAsync($"otp:cd:{userId}:{channel}"); /* allow immediate resend */ log.LogError(ex, "OTP delivery failed"); }
```
(Inject `ILogger<OtpService>`.) Set `"Otp": { "Sender": "notification" }` in Identity's dev config. Keep the dev OTP endpoint — it still works because the code is stored in `dev:otp:*`.

### 3.9 React: the notification bell

`src/api/notifications.ts`
```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../lib/http";
import type { Paged } from "./products";

export type InboxItem = { id: string; title: string; body: string; deepLink?: string; read: boolean; createdAt: string };
export const useUnreadCount = (enabled: boolean) =>
  useQuery({ queryKey: ["unread"], enabled, refetchInterval: 30_000,                // polling now; server-sent events arrive with live tracking in Phase 4
    queryFn: async () => (await api.get<{ count: number }>("/me/notifications/unread-count")).data.count });
export const useInbox = (enabled: boolean) =>
  useQuery({ queryKey: ["inbox"], enabled, queryFn: async () => (await api.get<Paged<InboxItem>>("/me/notifications", { params: { page_size: 10 } })).data.items });
export function useMarkRead() {
  const qc = useQueryClient();
  return useMutation({ mutationFn: (id: string) => api.post(`/me/notifications/${id}/read`),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["unread"] }); qc.invalidateQueries({ queryKey: ["inbox"] }); } });
}
```
`src/components/NotificationBell.tsx`
```tsx
import { useState } from "react";
import { Link } from "react-router";
import { useAuth } from "../auth/AuthContext";
import { useInbox, useMarkRead, useUnreadCount } from "../api/notifications";

export default function NotificationBell() {
  const { me } = useAuth(); const [open, setOpen] = useState(false);
  const unread = useUnreadCount(!!me); const inbox = useInbox(!!me && open); const mark = useMarkRead();
  if (!me) return null;
  return (
    <div className="relative">
      <button onClick={() => setOpen((o) => !o)} aria-label="Notifications" className="relative p-2">
        🔔{!!unread.data && <span className="absolute -right-1 -top-1 rounded-full bg-red-600 px-1.5 text-xs text-white">{unread.data}</span>}
      </button>
      {open && (
        <ul className="absolute right-0 z-10 mt-2 w-80 rounded border bg-white shadow">
          {inbox.data?.length === 0 && <li className="p-3 text-sm text-gray-500">You're all caught up.</li>}
          {inbox.data?.map((n) => (
            <li key={n.id} className={`border-b p-3 text-sm ${n.read ? "" : "bg-blue-50"}`}>
              {/* text is rendered as TEXT, never as HTML → a hostile product name can't inject script */}
              <Link to={n.deepLink ?? "#"} onClick={() => { mark.mutate(n.id); setOpen(false); }}><b>{n.title}</b><br />{n.body}</Link>
            </li>))}
        </ul>)}
    </div>);
}
```
Add `<NotificationBell />` to the layout header and a `/account/notifications` page with the preference toggles (`GET/PUT /me/notification-preferences`).

### 3.10 Tests (add to your mandatory list)
Template rendering (placeholders, **HTML-escaping**, unknown key → empty) · preference respected (promo email off by default, security can't be disabled) · **dedupe** (same message id twice → one row) · enqueue skips users without a contact · **backoff & dead-letter** (fake sender that throws: attempts 1→5, statuses `Pending`… then `DeadLetter`, `next_attempt_at` grows) · **claim concurrency** (Testcontainers Postgres, two workers on 100 pending rows → each sent **exactly once**) · OTP endpoint rejects without the internal key · inbox/read/preferences IDOR (user B can't read or mark user A's items).

✅ **Check**
1. Start everything (`docker compose up -d` includes Mailpit; `python scripts/dev.py` includes `notifications`). Register a user → **`http://localhost:8025`** shows a **welcome email** and an **OTP email**; the phone OTP appears as a masked `[DEV SMS]` line in the Notification log. Verify using codes copied from Mailpit — no `[DEV OTP]` log line from Identity any more.
2. Buy something → email *"Order LM-… confirmed"* in Mailpit, an in-app item, and the React **bell** shows **1**; open it → click → lands on the order; the counter drops to 0.
3. Advance the order with the dev simulator → in-app updates ("out for delivery"…).
4. `PUT /me/notification-preferences` `{category:"orders", email:false, …}` → the next order sends **no email** but still shows in-app. `PUT` with `category:"security"` → 400.
5. **Retry test:** `docker compose stop mailpit`, trigger an email → row stays `Pending` with `attempts` 1, 2… and growing `next_attempt_at`; `start mailpit` → delivered. Set `Delivery:MaxAttempts` to 2 temporarily → `DeadLetter` → as the super admin (or a customer manager) call `GET /admin/notifications/dead-letter` → `POST …/requeue` → delivered.
6. **Outage isolation:** stop the Notification service → register/checkout still work (events wait in RabbitMQ); start it → messages flow. Registration with Notification **down** returns 202 and the OTP can be re-sent a moment later.
7. Two copies of the service running (`Workers:Enabled` true on both) → still each message once.

⚠️ **Pitfalls:** unescaped values in HTML email · logging OTP bodies · putting secrets (SMTP password, SMS key) in `appsettings.json` — use environment/secret store · forgetting that `FromSqlRaw` needs the **schema-qualified** table name `notifications.notification_messages` (the DB login has no `search_path` pointing at it) · sending emails **from a consumer inline** (slow SMTP would block the queue and retries would re-send) — that is why we enqueue and deliver separately · SMS for marketing (cost) — blocked by default preferences.

Update the known-gaps register: remove #2 (OTP logging). Add: *"Staff notifications (KYC queue, dead-letter alerts) arrive with the admin panel (Phase 3)."* Commit: `feat(notifications): templates, preferences, delivery worker, otp, inbox`. **ADR-019** (*Central notification service; OTP via sync internal call; SMTP relay adapter*).

---

# PART F — SPRINT 4: RECONCILER + WORKERS (Week 14, in parallel with Sprint 3's tail)

🎯 **Goal:** detect and heal Redis↔Postgres stock drift automatically (known-gap #6), and make background loops runnable as a **separate deployment** from the web API.

### 4.1 The stock reconciler

🧠 **Why drift exists.** The Inventory "reserve" does *Redis decrement → Postgres insert*. A crash between the two leaves Redis lower than reality (stock "leaked"); a Redis restart from an old snapshot leaves it too high. Postgres is the **accounting truth**, Redis the **operational** one, so periodically **compare and correct Redis toward Postgres** (architecture doc §4.4: *"a nightly reconciler compares sums and alerts on drift > 0.5 %"*).

🧠 **Two safety rules for a healer that edits live data:** (1) **double-check** — a reservation in flight looks like drift for a few milliseconds, so re-read after a short pause and skip rows touched in the last minute; (2) **compare-and-set** — only overwrite Redis if it *still holds the exact drifted value we observed*, so a customer reserving at that instant is never clobbered.

`Workers/StockReconciler.cs` (Inventory)
```csharp
using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Stock;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace LankaMart.Inventory.Api.Workers;

public sealed record ReconcileReport(int Checked, int Drifted, int Healed, double DriftPercent);

public sealed class ReconcilerService(InventoryDb db, IConnectionMultiplexer redis, IConfiguration cfg, TimeProvider clock, ILogger<ReconcilerService> log)
{
    // "heal only if Redis still holds the value I saw" — atomic in Redis
    private const string CasLua = "if redis.call('GET', KEYS[1]) == ARGV[1] then redis.call('SET', KEYS[1], ARGV[2]) return 1 end return 0";

    public async Task<ReconcileReport> RunAsync(CancellationToken ct)
    {
        var autoHeal = cfg.GetValue("Reconciler:AutoHeal", true);
        var quietSince = clock.GetUtcNow().AddSeconds(-60);                 // skip items changed in the last minute
        var r = redis.GetDatabase(); int checkedN = 0, drifted = 0, healed = 0;

        for (var offset = 0; ; offset += 500)
        {
            var batch = await db.Items.AsNoTracking().Where(i => i.UpdatedAt < quietSince).OrderBy(i => i.VariantId).Skip(offset).Take(500)
                                .Select(i => new { i.VariantId, Avail = i.QtyOnHand - i.QtyReserved }).ToListAsync(ct);
            if (batch.Count == 0) break;
            foreach (var item in batch)
            {
                checkedN++;
                var key = RedisStock.Key(item.VariantId);
                var seen = await r.StringGetAsync(key);
                if (!seen.HasValue || (long)seen == item.Avail) continue;       // missing counter = cold cache (lazily reloaded), not drift

                await Task.Delay(500, ct);                                       // double-check: let any in-flight reservation finish
                var fresh = await db.Items.AsNoTracking().Where(i => i.VariantId == item.VariantId).Select(i => i.QtyOnHand - i.QtyReserved).SingleAsync(ct);
                var now = await r.StringGetAsync(key);
                if (!now.HasValue || (long)now == fresh) continue;

                drifted++;
                log.LogWarning("STOCK DRIFT variant {Variant}: redis={Redis} postgres={Pg}", item.VariantId, (long)now, fresh);
                if (autoHeal && (long)await r.ScriptEvaluateAsync(CasLua, [key], [now.ToString(), fresh.ToString()]) == 1) healed++;
            }
        }
        var pct = checkedN == 0 ? 0 : 100.0 * drifted / checkedN;
        if (pct > 0.5) log.LogError("STOCK DRIFT {Pct:F2}% of {Checked} items — above the 0.5% alert threshold, investigate the cause", pct, checkedN);
        else log.LogInformation("Reconciler: checked {Checked}, drifted {Drifted}, healed {Healed}", checkedN, drifted, healed);
        return new(checkedN, drifted, healed, pct);
    }
}

public sealed class StockReconciler(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<StockReconciler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(cfg.GetValue("Reconciler:EveryMinutes", 60)));
        do
        {
            try { using var s = scopes.CreateScope(); await s.ServiceProvider.GetRequiredService<ReconcilerService>().RunAsync(stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Reconciler run failed"); }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
```
Register in `Program.cs`: `builder.Services.AddScoped<ReconcilerService>();` and add an on-demand trigger next to the other internal endpoints:
```csharp
i.MapPost("/reconcile", async (ReconcilerService r, CancellationToken ct) => Results.Ok(await r.RunAsync(ct)));    // internal group: needs the internal key
```

### 4.2 Make every background loop switchable

In **Inventory** (`ReservationSweeper`, `StockReconciler`), **Orders** (`ExpireStaleOrdersWorker`) — Notifications already has it — register workers only when allowed:
```csharp
if (cfg.GetValue("Workers:Enabled", true))
{
    builder.Services.AddHostedService<ReservationSweeper>();
    builder.Services.AddHostedService<StockReconciler>();
}
```
🧠 **Why split workers from the API in Kubernetes?** You scale the **API** to match web traffic (6 replicas), but you want *one* (or two) background loops, not six. A worker crash/restart must not drop web requests, and a traffic spike must not starve your sweepers. Same image, two Deployments: **api** (`Workers__Enabled=false`, many replicas, behind the Service) and **worker** (`Workers__Enabled=true`, 1 replica, no Service). Our loops are *safe* to run on every replica (compare-and-set everywhere), so this is about efficiency and isolation, not correctness. *(MassTransit's outbox delivery service keeps running inside every API pod on purpose — it is lock-protected and you want events flowing as long as any pod is up.)*

### 4.3 Tests & checks
Tests: **healing** (set Redis wrong → reconcile → equals Postgres) · **CAS no-clobber** (change Redis between observe and heal → untouched) · **skips recent rows** · **cold counter ignored** · **alert threshold** (1 of 100 drifted → error log).

✅ **Check**
1. Pick a variant with 50 available. `docker exec -it lm-redis redis-cli SET stock:<variantId> 7` → `POST http://localhost:5104/api/v1/internal/inventory/reconcile` (internal key header) → response `drifted: 1, healed: 1`; the log shows a `STOCK DRIFT` warning; Redis is back to 50.
2. Simulate the crash-leak: `DECRBY stock:<id> 3` → reconcile heals it. `DEL stock:<id>` → **no** drift reported (lazy reload).
3. Drift in 1 of ≈100 variants → `ERROR … above the 0.5% alert threshold`.
4. Run the service with `Workers__Enabled=false` → sweeper/reconciler never start (no logs); the API works.
5. Run the race test from Phase 1 again, then reconcile → **0 drift** (a healthy system reports nothing).

⚠️ **Pitfalls:** healing without the double-check/CAS (you will "fix" legitimate in-flight reservations and oversell) · running the reconciler at full speed on a huge table (batch + `Delay` as above) · hiding drift by always healing silently — **log it and alert**, because drift means a bug or an outage you should understand.

Update known-gaps: remove #6. Commit: `feat(inventory): stock reconciler and worker switches`. **ADR-020** (*Reconcile Redis toward Postgres with double-check + CAS; workers separable*).

---

# PART G — SPRINT 5: KUBERNETES, HELM, ARGO CD, GITOPS (Weeks 15–16)

🎯 **Goal:** run LankaMart on a Kubernetes cluster (k3s) where **a `git push` deploys it** — with health probes, rolling updates, autoscaling, network firewalls, migration jobs, and image scanning in the pipeline.

🧠 **Plan the learning in three steps** (each one is a milestone — stop and celebrate each):
**5a** deploy **by hand** with `kubectl`/`helm` (you learn what each object does) → **5b** let **Argo CD** deploy from Git (GitOps) → **5c** make **CI** build, scan and publish images and update Git automatically.

### 5.0 Tools and a disposable local cluster

Install: **kubectl** (talk to clusters), **helm** (templating; v3 or v4 both work with this guide), **k3d** (runs k3s clusters *inside Docker* — a real Kubernetes in 30 s, deleted with one command), **k9s** (a friendly terminal UI), optionally **kubectx**. Verify with `kubectl version --client`, `helm version`, `k3d version`.

```bash
k3d cluster create lankamart --agents 1 --port "8080:80@loadbalancer"    # cluster + port 8080 on your laptop → the cluster's Traefik ingress
kubectl get nodes                                  # 2 nodes Ready (1 server, 1 agent)
kubectl get pods -A                                # coredns, traefik, metrics-server … = k3s's bundled add-ons
kubectl top nodes                                  # works because metrics-server is bundled (needed by autoscaling)
```
🧠 *Why Traefik and not NGINX?* k3s ships **Traefik** as its ingress controller. The popular `ingress-nginx` project was **retired in March 2026** (no more fixes) — do not install it. The Kubernetes `Ingress` API itself still works and is what we use; the long-term successor is the **Gateway API** (Traefik supports it) — a Phase 6 migration, noted in the ADR. Remember: your **YARP gateway** does the real routing; Traefik just forwards port 80 to it.

**Cheap real staging (optional, later):** a ~$20/month VPS with 4 GB RAM + `curl -sfL https://get.k3s.io | sh -` gives a real k3s node; copy `/etc/rancher/k3s/k3s.yaml` to your laptop as a kubeconfig (replace `127.0.0.1` with the server's address) and open only ports 80/443/6443 in the firewall. Everything below is identical.

### 5.1 Repository layout for deployment

```
deploy/
├── k8s/                          plain manifests (Argo app "lm-infra")
│   ├── 00-namespace.yaml
│   ├── network-policies.yaml
│   └── infra/  postgres.yaml  redis.yaml  rabbitmq.yaml  mailpit.yaml
├── helm/
│   ├── service/                  ONE generic chart used by every service
│   │   ├── Chart.yaml  values.yaml
│   │   └── templates/  _helpers.tpl  deployment.yaml  worker.yaml  service.yaml  hpa.yaml  pdb.yaml  ingress.yaml  migration-job.yaml
│   └── values/                   common.yaml  gateway.yaml  identity.yaml  users.yaml  catalog.yaml  inventory.yaml  orders.yaml  payments.yaml  notifications.yaml  recommender.yaml
└── argocd/                       infra-app.yaml  services-appset.yaml
scripts/  k8s-secrets.sh  k8s-build-import.sh
```

### 5.2 Prepare the code for a non-Development environment

Staging must behave like production (no auto-migrate, no Scalar) **but** you still need seeded admins and the OTP/simulator tools for smoke and load tests. Introduce one explicit switch, **`Features:DevTools`**, and a guard that makes it impossible to enable in Production.

`BuildingBlocks/Platform/DevTools.cs`
```csharp
namespace LankaMart.BuildingBlocks;

public static class DevTools
{
    /// <summary>True in Development, or in Staging when Features:DevTools=true. NEVER true in Production (startup refuses).</summary>
    public static bool Enabled(this WebApplication app) => app.Environment.IsDevelopment() || app.Configuration.GetValue("Features:DevTools", false);
    public static bool Enabled(this IHostEnvironment env, IConfiguration cfg) => env.IsDevelopment() || cfg.GetValue("Features:DevTools", false);
}
```
In `AddPlatformDefaults` (first lines): `if (builder.Environment.IsProduction() && builder.Configuration.GetValue("Features:DevTools", false)) throw new InvalidOperationException("Features:DevTools must never be enabled in Production.");`

Replace the Phase 1 `IsDevelopment()` checks:

| Where | Change |
|---|---|
| `UsePlatformDefaults` (Scalar/OpenAPI) | keep **Development only** (docs are not needed in staging) |
| Identity `MapDevEndpoints` + `OtpService` (`dev:otp:*` key) | `app.Enabled()` / `env.Enabled(cfg)` |
| Identity `IdentitySeeder` (dev admins) | `env.Enabled(cfg)` |
| Orders dev simulator | `app.Enabled()` |
| Payments failure lab | `app.Enabled()` |
| Gateway dev routes | add `appsettings.Staging.json` containing the same three `dev-*` routes as `appsettings.Development.json` |

Also in `appsettings.json` of Inventory/Orders etc. nothing changes. `Database:AutoMigrate` defaults to **false** outside Development (Sprint 1) — migrations will run as a Job.

### 5.3 Namespace, secrets, and the infrastructure (plain manifests)

`deploy/k8s/00-namespace.yaml`
```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: lm-staging
  labels: { pod-security.kubernetes.io/enforce: baseline }
```

**Secrets are never in Git.** `scripts/k8s-secrets.sh` creates them once (re-running keeps existing values, so DB passwords don't change under a live database). Use Git Bash/WSL on Windows.
```bash
#!/usr/bin/env bash
# usage: NS=lm-staging ./scripts/k8s-secrets.sh        (optional: GHCR_USER=… GHCR_TOKEN=… to create the image pull secret)
set -euo pipefail
NS="${NS:-lm-staging}"
rand() { openssl rand -base64 36 | tr -d '/+=\n' | cut -c1-28; }
kubectl get ns "$NS" >/dev/null 2>&1 || kubectl create ns "$NS"

ensure() {   # ensure <secret-name> key=value ...   → create only if absent
  local name="$1"; shift
  if kubectl -n "$NS" get secret "$name" >/dev/null 2>&1; then echo "secret/$name exists (kept)"; return; fi
  local args=(); for kv in "$@"; do args+=(--from-literal="$kv"); done
  kubectl -n "$NS" create secret generic "$name" "${args[@]}"
}

SERVICES=(identity users catalog inventory orders payments notifications)
init_args=("POSTGRES_PASSWORD=$(rand)")
for s in "${SERVICES[@]}"; do
  pw="$(rand)"; up="$(echo "$s" | tr a-z A-Z)"
  init_args+=("DB_PASSWORD_${up}=${pw}")
  ensure "conn-$s" "value=Host=postgres;Database=${s}_db;Username=${s}_svc;Password=${pw};Maximum Pool Size=20;Minimum Pool Size=1"   # one full connection string per service; the pool cap matters when you scale out (see Sprint 6)
done
ensure postgres-init "${init_args[@]}"       # NOTE: conn-* use the passwords generated in THIS run; on a re-run with an existing postgres-init they are kept in sync only if both are new — if you must regenerate, delete ALL of them and the postgres PVC.
ensure rabbitmq "username=lankamart" "password=$(rand)"
ensure internal-api-key "value=$(rand)"
ensure mock-webhook-secret "value=$(rand)"

if ! kubectl -n "$NS" get secret jwt-signing-key >/dev/null 2>&1; then
  openssl genrsa -out /tmp/jwt-private.pem 2048 2>/dev/null
  kubectl -n "$NS" create secret generic jwt-signing-key --from-file=jwt-private.pem=/tmp/jwt-private.pem
  rm -f /tmp/jwt-private.pem
fi
if [[ -n "${GHCR_USER:-}" && -n "${GHCR_TOKEN:-}" ]]; then
  kubectl -n "$NS" get secret ghcr-pull >/dev/null 2>&1 || kubectl -n "$NS" create secret docker-registry ghcr-pull --docker-server=ghcr.io --docker-username="$GHCR_USER" --docker-password="$GHCR_TOKEN"
fi
kubectl -n "$NS" create configmap pg-init --from-file=infra/postgres/init/01-databases.sh --dry-run=client -o yaml | kubectl apply -f -
echo "done"
```
⚠️ If the `conn-*` and `postgres-init` secrets ever get out of sync (e.g. you deleted one), delete **all of them plus the Postgres PVC** and start clean — staging data is disposable.

`deploy/k8s/infra/postgres.yaml`
```yaml
apiVersion: v1
kind: Service
metadata: { name: postgres, namespace: lm-staging }
spec: { clusterIP: None, selector: { app: postgres }, ports: [{ name: pg, port: 5432 }] }
---
apiVersion: apps/v1
kind: StatefulSet
metadata: { name: postgres, namespace: lm-staging }
spec:
  serviceName: postgres
  replicas: 1
  selector: { matchLabels: { app: postgres } }
  template:
    metadata: { labels: { app: postgres, part-of: lankamart-infra } }
    spec:
      containers:
        - name: postgres
          image: postgres:17-alpine
          args: ["postgres", "-c", "max_connections=300"]     # default 100 is too low once services scale out (Sprint 6)
          ports: [{ containerPort: 5432 }]
          env:
            - { name: POSTGRES_USER, value: postgres }
            - { name: PGDATA, value: /var/lib/postgresql/data/pgdata }
          envFrom: [{ secretRef: { name: postgres-init } }]        # POSTGRES_PASSWORD + DB_PASSWORD_<SVC>
          readinessProbe: { exec: { command: ["pg_isready", "-U", "postgres"] }, initialDelaySeconds: 5, periodSeconds: 5 }
          resources: { requests: { cpu: 100m, memory: 256Mi }, limits: { memory: 1Gi } }
          volumeMounts:
            - { name: data, mountPath: /var/lib/postgresql/data }
            - { name: init, mountPath: /docker-entrypoint-initdb.d }
      volumes:
        - { name: init, configMap: { name: pg-init } }
  volumeClaimTemplates:
    - metadata: { name: data }
      spec: { accessModes: [ReadWriteOnce], resources: { requests: { storage: 5Gi } } }
```
`redis.yaml` and `rabbitmq.yaml` follow the same shape — **write them yourself from this template** (good practice): Redis: image `redis:7-alpine`, `args: ["--appendonly","yes"]`, port 6379, probe `redis-cli ping`, PVC 1Gi, label `app: redis`. RabbitMQ: image `rabbitmq:4-management-alpine`, ports 5672 (+15672), env `RABBITMQ_DEFAULT_USER/PASS` from secret `rabbitmq` (`valueFrom.secretKeyRef` keys `username`/`password`), probe `rabbitmq-diagnostics -q ping`, PVC 2Gi, label `app: rabbitmq`. Each needs a headless `Service` named `redis` / `rabbitmq`. `mailpit.yaml`: a Deployment of `axllent/mailpit` + Service `mailpit` (ports 1025, 8025) — **staging only**. *(Production data stores are managed services or operators — Phase 6/7. These single-replica StatefulSets are for staging.)*

**NetworkPolicies** — a firewall between pods. `deploy/k8s/network-policies.yaml`:
```yaml
# 1) Deny everything by default. Nothing talks to anything until we say so.
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: default-deny-all, namespace: lm-staging }
spec: { podSelector: {}, policyTypes: [Ingress, Egress] }
---
# 2) Everyone may do DNS lookups.
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: allow-dns, namespace: lm-staging }
spec:
  podSelector: {}
  policyTypes: [Egress]
  egress:
    - to: [{ namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: kube-system } } }]
      ports: [{ protocol: UDP, port: 53 }, { protocol: TCP, port: 53 }]
---
# 3) LankaMart pods may CONNECT OUT to other pods in this namespace (who may be connected TO is controlled below).
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: allow-egress-in-namespace, namespace: lm-staging }
spec:
  podSelector: { matchExpressions: [{ key: part-of, operator: In, values: [lankamart, lankamart-infra] }] }
  policyTypes: [Egress]
  egress: [{ to: [{ podSelector: {} }] }]
---
# 4) The ONLY thing allowed into the gateway is the ingress controller (Traefik lives in kube-system).
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: gateway-from-ingress, namespace: lm-staging }
spec:
  podSelector: { matchLabels: { app: gateway } }
  policyTypes: [Ingress]
  ingress: [{ from: [{ namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: kube-system } } }] }]
---
# 5) Services accept traffic from the gateway and from each other (internal REST + JWKS fetches). Nobody else — not even other namespaces.
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: services-from-lankamart, namespace: lm-staging }
spec:
  podSelector: { matchExpressions: [{ key: app, operator: In, values: [identity, users, catalog, inventory, orders, payments, notifications, recommender] }] }
  policyTypes: [Ingress]
  ingress: [{ from: [{ podSelector: { matchLabels: { part-of: lankamart } } }] }]
---
# 6) Data stores accept connections only from LankaMart pods.
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: infra-from-lankamart, namespace: lm-staging }
spec:
  podSelector: { matchLabels: { part-of: lankamart-infra } }
  policyTypes: [Ingress]
  ingress: [{ from: [{ podSelector: { matchLabels: { part-of: lankamart } } }] }]
---
# 7) Notifications must reach the internet for real SMTP/SMS providers (443 and 587). Remove if you only use in-cluster Mailpit.
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: notifications-egress-internet, namespace: lm-staging }
spec:
  podSelector: { matchLabels: { app: notifications } }
  policyTypes: [Egress]
  egress: [{ to: [{ ipBlock: { cidr: 0.0.0.0/0 } }], ports: [{ protocol: TCP, port: 443 }, { protocol: TCP, port: 587 }] }]
```
🧠 *Reading it:* **Egress** = traffic *leaving* a pod, **Ingress** = traffic *entering*. Policies are **additive allow-lists** on top of a deny-all. Result: a hacked Catalog pod cannot call Payments or the databases of other services, and nothing outside the namespace can talk to any service directly. (k3s/k3d enforce policies out of the box.) *(The gateway also needs to reach the services: rule 3 lets it connect out, rule 5 lets services accept it.)*

### 5.4 The generic Helm chart

`deploy/helm/service/Chart.yaml`
```yaml
apiVersion: v2
name: service
description: Generic chart for every LankaMart service
type: application
version: 0.1.0
appVersion: "1.0"
```
`deploy/helm/service/values.yaml` (defaults; each service overrides a few)
```yaml
name: ""                         # REQUIRED, e.g. "catalog"
image: { repository: "", tag: latest, pullPolicy: IfNotPresent }
imagePullSecrets: []             # e.g. [{ name: ghcr-pull }]
replicaCount: 2                  # ignored while hpa.enabled (the HPA owns the replica count)
port: 8080
env: {}                          # plain settings   → container env
secretEnv: {}                    # NAME: { secret: <secret-name>, key: <key> }
secretVolumes: []                # - { name: jwt, secret: jwt-signing-key, mountPath: /keys }
probes: { live: /health/live, ready: /health/ready }
resources:
  requests: { cpu: 100m, memory: 192Mi }
  limits: { memory: 384Mi }      # memory limit yes; NO cpu limit — cpu limits throttle .NET and hurt latency (requests are what scheduling & HPA use)
hpa: { enabled: true, minReplicas: 2, maxReplicas: 6, targetCPU: 70 }
pdb: { enabled: true, minAvailable: 1 }
ingress: { enabled: false, className: traefik, host: "", path: / }
migration: { enabled: false }    # run `--migrate-only` as a pre-deploy Job
worker: { enabled: false }       # a second Deployment running only background loops (Workers__Enabled=true)
```
`templates/_helpers.tpl` — the shared pod template (API, worker and migration job all use it):
```yaml
{{- define "svc.labels" -}}
app: {{ .Values.name }}
part-of: lankamart
{{- end }}

{{- define "svc.pod" -}}
{{- $v := .ctx.Values -}}
metadata:
  labels:
    app: {{ $v.name }}
    component: {{ .role }}
    part-of: lankamart
spec:
  {{- if eq .role "migrate" }}
  restartPolicy: Never
  {{- end }}
  {{- with $v.imagePullSecrets }}
  imagePullSecrets:
    {{- toYaml . | nindent 4 }}
  {{- end }}
  securityContext:
    runAsNonRoot: true
    seccompProfile: { type: RuntimeDefault }
  containers:
    - name: app
      image: "{{ $v.image.repository }}:{{ $v.image.tag }}"
      imagePullPolicy: {{ $v.image.pullPolicy }}
      {{- if eq .role "migrate" }}
      args: ["--migrate-only"]
      {{- else }}
      ports:
        - { name: http, containerPort: {{ $v.port }} }
      {{- end }}
      env:
        {{- range $k, $val := $v.env }}
        - name: {{ $k }}
          value: {{ $val | quote }}
        {{- end }}
        {{- if eq .role "worker" }}
        - { name: Workers__Enabled, value: "true" }
        {{- else if eq .role "api" }}
        - { name: Workers__Enabled, value: {{ ternary "false" "true" $v.worker.enabled | quote }} }
        {{- end }}
        {{- range $k, $s := $v.secretEnv }}
        - name: {{ $k }}
          valueFrom:
            secretKeyRef: { name: {{ $s.secret }}, key: {{ $s.key }} }
        {{- end }}
      {{- if ne .role "migrate" }}
      startupProbe:  { httpGet: { path: {{ $v.probes.live }}, port: http }, failureThreshold: 30, periodSeconds: 2 }
      livenessProbe: { httpGet: { path: {{ $v.probes.live }}, port: http }, periodSeconds: 10, failureThreshold: 3 }
      readinessProbe: { httpGet: { path: {{ $v.probes.ready }}, port: http }, periodSeconds: 5, failureThreshold: 3 }
      {{- end }}
      resources:
        {{- toYaml $v.resources | nindent 8 }}
      securityContext:
        allowPrivilegeEscalation: false
        readOnlyRootFilesystem: true
        capabilities: { drop: [ALL] }
      volumeMounts:
        - { name: tmp, mountPath: /tmp }
        {{- range $v.secretVolumes }}
        - { name: {{ .name }}, mountPath: {{ .mountPath }}, readOnly: true }
        {{- end }}
  volumes:
    - { name: tmp, emptyDir: {} }
    {{- range $v.secretVolumes }}
    - { name: {{ .name }}, secret: { secretName: {{ .secret }} } }
    {{- end }}
{{- end }}
```
`templates/deployment.yaml`
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ .Values.name }}
  labels: {{- include "svc.labels" . | nindent 4 }}
spec:
  {{- if not .Values.hpa.enabled }}
  replicas: {{ .Values.replicaCount }}
  {{- end }}
  strategy:
    type: RollingUpdate
    rollingUpdate: { maxUnavailable: 0, maxSurge: 1 }      # start a NEW pod, wait until READY, only then retire an old one → zero-downtime
  selector:
    matchLabels: { app: {{ .Values.name }}, component: api }
  template:
    {{- include "svc.pod" (dict "ctx" . "role" "api") | nindent 4 }}
```
`templates/worker.yaml`
```yaml
{{- if .Values.worker.enabled }}
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ .Values.name }}-worker
  labels: {{- include "svc.labels" . | nindent 4 }}
spec:
  replicas: 1
  strategy: { type: Recreate }
  selector:
    matchLabels: { app: {{ .Values.name }}, component: worker }
  template:
    {{- include "svc.pod" (dict "ctx" . "role" "worker") | nindent 4 }}
{{- end }}
```
`templates/service.yaml`
```yaml
apiVersion: v1
kind: Service
metadata:
  name: {{ .Values.name }}
  labels: {{- include "svc.labels" . | nindent 4 }}
spec:
  selector: { app: {{ .Values.name }}, component: api }     # only API pods receive traffic (workers have no Service)
  ports:
    - { name: http, port: 80, targetPort: http }             # other pods call http://catalog (port 80)
```
`templates/hpa.yaml`
```yaml
{{- if .Values.hpa.enabled }}
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata: { name: {{ .Values.name }} }
spec:
  scaleTargetRef: { apiVersion: apps/v1, kind: Deployment, name: {{ .Values.name }} }
  minReplicas: {{ .Values.hpa.minReplicas }}
  maxReplicas: {{ .Values.hpa.maxReplicas }}
  metrics:
    - type: Resource
      resource: { name: cpu, target: { type: Utilization, averageUtilization: {{ .Values.hpa.targetCPU }} } }
  behavior:
    scaleDown: { stabilizationWindowSeconds: 120 }           # don't flap: wait 2 min of calm before removing pods
{{- end }}
```
`templates/pdb.yaml`
```yaml
{{- if .Values.pdb.enabled }}
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata: { name: {{ .Values.name }} }
spec:
  minAvailable: {{ .Values.pdb.minAvailable }}               # node maintenance may never take the last healthy pod
  selector: { matchLabels: { app: {{ .Values.name }}, component: api } }
{{- end }}
```
`templates/ingress.yaml`
```yaml
{{- if .Values.ingress.enabled }}
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata: { name: {{ .Values.name }} }
spec:
  ingressClassName: {{ .Values.ingress.className }}
  rules:
    - http:
        paths:
          - path: {{ .Values.ingress.path }}
            pathType: Prefix
            backend: { service: { name: {{ .Values.name }}, port: { number: 80 } } }
      {{- if .Values.ingress.host }}
      host: {{ .Values.ingress.host }}
      {{- end }}
{{- end }}
```
`templates/migration-job.yaml`
```yaml
{{- if .Values.migration.enabled }}
apiVersion: batch/v1
kind: Job
metadata:
  name: {{ .Values.name }}-migrate
  annotations:
    argocd.argoproj.io/hook: PreSync                           # Argo CD: run BEFORE the new version is rolled out
    argocd.argoproj.io/hook-delete-policy: BeforeHookCreation
    helm.sh/hook: pre-install,pre-upgrade                      # plain `helm upgrade` behaves the same
    helm.sh/hook-delete-policy: before-hook-creation
spec:
  backoffLimit: 6                                              # retry while Postgres is still starting
  ttlSecondsAfterFinished: 600
  template:
    {{- include "svc.pod" (dict "ctx" . "role" "migrate") | nindent 4 }}
{{- end }}
```
Validate the chart **before** using it: `helm lint deploy/helm/service` and `helm template catalog deploy/helm/service -f deploy/helm/values/common.yaml -f deploy/helm/values/catalog.yaml | less` — read the generated YAML; this is the fastest way to learn Helm and to catch indentation mistakes.

### 5.5 Values files

`deploy/helm/values/common.yaml` — settings **every** service shares:
```yaml
imagePullSecrets: [{ name: ghcr-pull }]
env:
  ASPNETCORE_ENVIRONMENT: Staging
  Features__DevTools: "true"                 # staging only: seeded admins, OTP lookup, simulator, failure lab. Startup REFUSES this in Production.
  Database__AutoMigrate: "false"             # migrations run as a Job
  Redis__ConnectionString: redis:6379
  RabbitMq__Host: rabbitmq
  RabbitMq__Username: lankamart
  Auth__Authority: http://identity
  Auth__Issuer: lankamart-identity
  Auth__Audience: lankamart-api
  Auth__RequireHttps: "false"                # TLS ends at the ingress; in-cluster mTLS arrives in Phase 6
  Services__Catalog: http://catalog
  Services__Inventory: http://inventory
  Services__Payments: http://payments
  Services__Notifications: http://notifications
  Otel__Endpoint: ""
secretEnv:
  RabbitMq__Password: { secret: rabbitmq, key: password }
  Internal__ApiKey: { secret: internal-api-key, key: value }
```
Per service (examples — create the rest the same way):

`catalog.yaml`
```yaml
name: catalog
image: { repository: ghcr.io/<owner>/lankamart-catalog, tag: latest }
migration: { enabled: true }
secretEnv:
  ConnectionStrings__Default: { secret: conn-catalog, key: value }
```
`identity.yaml` — adds the signing key and its own settings
```yaml
name: identity
image: { repository: ghcr.io/<owner>/lankamart-identity, tag: latest }
migration: { enabled: true }
env: { Jwt__KeyPath: /keys/jwt-private.pem, Otp__Sender: notification, Seed__SuperAdminEmail: admin@lankamart.local, Seed__SuperAdminPassword: "ChangeMe#12345678", Seed__SellerManagerEmail: sellermgr@lankamart.local, Seed__SellerManagerPassword: "ChangeMe#12345678" }
secretEnv: { ConnectionStrings__Default: { secret: conn-identity, key: value } }
secretVolumes: [{ name: jwt, secret: jwt-signing-key, mountPath: /keys }]
```
*(Those seed passwords exist only because `Features__DevTools` is on in staging; they're throwaway. Production never seeds admins — Phase 6.)*
`gateway.yaml`: `name: gateway`, `ingress: { enabled: true }`, `env` with the `ReverseProxy__Clusters__<name>__Destinations__d1__Address: http://<name>/` entries (identity, users, catalog, inventory, orders, payments, notifications, recommender) and `Cors__Origins__0: http://localhost:5173`; no `migration`, no DB secret; add `Redis` (already in common) and the `/api/v1/me` + `/api/v1/admin/notifications` routes live in its `appsettings.json`.
`inventory.yaml`: DB secret, `migration.enabled: true`, **`worker: { enabled: true }`**, `env: { Inventory__DevDefaultStock: "50" }` (staging convenience).
`orders.yaml`: DB secret, migration, `worker: { enabled: true }`, `env: { Orders__PaymentWindowMinutes: "15" }`.
`payments.yaml`: DB secret, migration, `secretEnv` `Mock__WebhookSecret` from `mock-webhook-secret`, `env: { Mock__WebhookUrl: http://payments/api/v1/webhooks/mock }`.
`notifications.yaml`: DB secret, migration, `worker: { enabled: true }`, `env: { Email__Host: mailpit, Email__Port: "1025", Email__From: "LankaMart <no-reply@lankamart.local>" }`.
`users.yaml`: DB secret, migration.
`recommender.yaml`: `image.repository …-recommender`, `probes: { live: /health, ready: /health }`, `env: { CATALOG_URL: http://gateway/api/v1 }`, `hpa: { enabled: false }`, `replicaCount: 1`, `migration` absent.

### 5.6 Milestone 5a — deploy by hand

`scripts/k8s-build-import.sh` builds every image locally and loads it into the k3d cluster (no registry needed):
```bash
#!/usr/bin/env bash
set -euo pipefail
CLUSTER="${CLUSTER:-lankamart}"
build() {  # build <name> <csproj> <dll>
  docker build -f infra/docker/dotnet.Dockerfile --build-arg PROJECT="$2" --build-arg DLL="$3" -t "lankamart/$1:dev" .
  k3d image import "lankamart/$1:dev" -c "$CLUSTER"
}
build gateway       src/gateway/LankaMart.Gateway/LankaMart.Gateway.csproj                                   LankaMart.Gateway.dll
build identity      src/services/identity/LankaMart.Identity.Api/LankaMart.Identity.Api.csproj               LankaMart.Identity.Api.dll
build users         src/services/users/LankaMart.Users.Api/LankaMart.Users.Api.csproj                        LankaMart.Users.Api.dll
build catalog       src/services/catalog/LankaMart.Catalog.Api/LankaMart.Catalog.Api.csproj                  LankaMart.Catalog.Api.dll
build inventory     src/services/inventory/LankaMart.Inventory.Api/LankaMart.Inventory.Api.csproj            LankaMart.Inventory.Api.dll
build orders        src/services/orders/LankaMart.Orders.Api/LankaMart.Orders.Api.csproj                     LankaMart.Orders.Api.dll
build payments      src/services/payments/LankaMart.Payments.Api/LankaMart.Payments.Api.csproj               LankaMart.Payments.Api.dll
build notifications src/services/notifications/LankaMart.Notifications.Api/LankaMart.Notifications.Api.csproj LankaMart.Notifications.Api.dll
docker build -t lankamart/recommender:dev src/ml/recommender && k3d image import lankamart/recommender:dev -c "$CLUSTER"
```
Then:
```bash
NS=lm-staging ./scripts/k8s-secrets.sh
kubectl apply -f deploy/k8s/00-namespace.yaml
kubectl apply -R -f deploy/k8s/infra/ && kubectl apply -f deploy/k8s/network-policies.yaml
kubectl -n lm-staging rollout status statefulset/postgres statefulset/redis statefulset/rabbitmq
./scripts/k8s-build-import.sh

for s in identity users catalog inventory orders payments notifications gateway recommender; do
  helm upgrade --install "$s" deploy/helm/service -n lm-staging \
    -f deploy/helm/values/common.yaml -f "deploy/helm/values/$s.yaml" \
    --set image.repository="lankamart/$s" --set image.tag=dev --set image.pullPolicy=Never \
    --set 'imagePullSecrets=null'                 # local images need no pull secret
done
kubectl -n lm-staging get pods -w
```
*(Order matters only loosely: services wait for databases (migration Job retries) and for each other (readiness).)*

✅ **Check 5a** — *work through all of these; this is where Kubernetes becomes real*
1. `kubectl -n lm-staging get pods` → every pod `Running`/`Ready`; migration Jobs `Completed`. If not: `kubectl -n lm-staging describe pod <p>` (events at the bottom tell the story) and `kubectl logs <p>`.
2. `curl http://localhost:8080/api/v1/categories` → the 8 categories **through Traefik → gateway → catalog**. Run `python scripts/seed_demo.py` after pointing it at `http://localhost:8080/api/v1` (make `BASE` an environment variable) → products appear.
3. **Self-healing:** `kubectl -n lm-staging delete pod -l app=catalog` → pods are recreated; `curl` keeps working (2 replicas + readiness).
4. **Rolling update:** `helm upgrade …` with a changed `--set env.FOO=bar` and watch `kubectl rollout status deployment/orders` — old pods are replaced one at a time, `curl` in a loop never fails (`while true; do curl -s -o /dev/null -w "%{http_code}\n" localhost:8080/api/v1/categories; sleep 0.3; done`). `kubectl rollout undo deployment/orders` rolls back.
5. **Probes:** `kubectl -n lm-staging scale statefulset postgres --replicas=0` → services turn *not ready* (readiness fails) and stop getting traffic but are **not restarted** (liveness is only "process alive"); scale Postgres back → they recover. (That behaviour is exactly why liveness and readiness are separate endpoints.)
6. **NetworkPolicy proof:** `kubectl -n lm-staging run probe --rm -it --restart=Never --image=curlimages/curl -- curl -m 3 http://identity/api/v1/ping` → **times out** (unlabelled pod blocked). Add `--labels=part-of=lankamart` → it connects.
7. `kubectl -n lm-staging top pods`, `kubectl get hpa` (targets show CPU %). Install **k9s** and explore (`:pods`, `l` for logs, `s` for shell).
8. Full buying flow works in staging (`customer1@lankamart.local` after seeding): register → OTP from **Mailpit** (`kubectl -n lm-staging port-forward svc/mailpit 8025:8025`) → buy → paid.
9. `kubectl -n lm-staging get pods -o wide` and confirm each container runs as non-root: `kubectl -n lm-staging exec deploy/catalog -- id` → `uid=1654`.

⚠️ **Pitfalls:** `ImagePullBackOff` → image name/tag wrong, not imported into k3d, or missing pull secret · `CreateContainerConfigError` → a referenced Secret key doesn't exist (`kubectl describe pod`) · `CrashLoopBackOff` → read `kubectl logs --previous` (usually a missing config key like `Auth`) · pods stuck `Pending` → not enough CPU/memory on the (Docker-limited) cluster: raise Docker Desktop's memory to ≥ 8 GB or lower `replicaCount`/`minReplicas` · YAML indentation errors in the chart → `helm template` shows the exact line · `readOnlyRootFilesystem` + .NET: the writable `/tmp` volume is required (we mount it).

Commit: `feat(deploy): helm chart, manifests, network policies`. **ADR-021** (*k3s + Traefik; generic Helm chart; network policies default-deny*).

### 5.7 Milestone 5b — GitOps with Argo CD

🧠 **What changes:** you stop running `helm upgrade`. Argo CD (running *in* the cluster) watches your Git repo and continuously makes the cluster equal to it. Manual changes (`kubectl delete deployment`) are **reverted** (*self-heal*); removed files are **pruned**.

```bash
kubectl create namespace argocd
kubectl apply -n argocd --server-side -f https://raw.githubusercontent.com/argoproj/argo-cd/stable/manifests/install.yaml
kubectl -n argocd rollout status deployment/argocd-server
kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d ; echo     # the initial admin password
kubectl -n argocd port-forward svc/argocd-server 8081:443        # open https://localhost:8081  (user: admin; accept the self-signed cert)
```
In the UI: **Settings → Repositories → Connect repo** (HTTPS, your GitHub username, a fine-grained **read-only** token) — needed because the repo is private.

`deploy/argocd/infra-app.yaml`
```yaml
apiVersion: argoproj.io/v1alpha1
kind: Application
metadata: { name: lm-infra, namespace: argocd }
spec:
  project: default
  source: { repoURL: https://github.com/<you>/lankamart.git, targetRevision: main, path: deploy/k8s, directory: { recurse: true } }
  destination: { server: https://kubernetes.default.svc, namespace: lm-staging }
  syncPolicy: { automated: { prune: true, selfHeal: true } }
```
`deploy/argocd/services-appset.yaml` — **one definition generates one Application per service**:
```yaml
apiVersion: argoproj.io/v1alpha1
kind: ApplicationSet
metadata: { name: lm-services, namespace: argocd }
spec:
  generators:
    - list:
        elements:
          - { name: identity }
          - { name: users }
          - { name: catalog }
          - { name: inventory }
          - { name: orders }
          - { name: payments }
          - { name: notifications }
          - { name: gateway }
          - { name: recommender }
  template:
    metadata: { name: 'lm-{{name}}' }
    spec:
      project: default
      source:
        repoURL: https://github.com/<you>/lankamart.git
        targetRevision: main
        path: deploy/helm/service
        helm: { valueFiles: ['../values/common.yaml', '../values/{{name}}.yaml'] }
      destination: { server: https://kubernetes.default.svc, namespace: lm-staging }
      syncPolicy:
        automated: { prune: true, selfHeal: true }
        retry: { limit: 5, backoff: { duration: 15s, factor: 2, maxDuration: 3m } }      # DB or neighbours may still be starting
```
Replace `<you>`; commit and push these files; then bootstrap **once**: `kubectl apply -f deploy/argocd/`.
For images from your own registry (next step) the values files must point at `ghcr.io/<owner>/lankamart-<svc>` with a real tag — the `latest` placeholders above get replaced by CI.

✅ **Check 5b**
1. Argo UI shows `lm-infra` and nine `lm-*` apps turning **Synced / Healthy**.
2. Edit `deploy/helm/values/catalog.yaml` (e.g., `hpa.minReplicas: 3`), commit, push → within ~3 min (or click *Refresh*) the Catalog Deployment changes. **That is a deploy.**
3. **Self-heal:** `kubectl -n lm-staging delete deployment catalog` → Argo recreates it within seconds. Manual `kubectl scale` edits get reverted.
4. **Rollback is `git revert`:** break something on purpose (bad env var), watch the app go *Degraded* (the rolling update stalls because new pods never become Ready — **old pods keep serving**), `git revert`, push → healthy again.
5. A migration `PreSync` Job runs before Orders updates (watch it in the Argo resource tree).

### 5.8 Milestone 5c — CI builds, scans, publishes, and updates Git

`.github/workflows/release.yml`
```yaml
name: release
on:
  push:
    branches: [main]
    paths: ['src/**', 'infra/docker/**']          # don't rebuild images for docs-only commits

permissions:
  contents: write         # to commit the new image tag
  packages: write         # to push to GHCR

jobs:
  images:
    runs-on: ubuntu-latest
    strategy:
      fail-fast: true
      matrix:
        include:
          - { name: gateway,       project: src/gateway/LankaMart.Gateway/LankaMart.Gateway.csproj,                                   dll: LankaMart.Gateway.dll }
          - { name: identity,      project: src/services/identity/LankaMart.Identity.Api/LankaMart.Identity.Api.csproj,               dll: LankaMart.Identity.Api.dll }
          - { name: users,         project: src/services/users/LankaMart.Users.Api/LankaMart.Users.Api.csproj,                        dll: LankaMart.Users.Api.dll }
          - { name: catalog,       project: src/services/catalog/LankaMart.Catalog.Api/LankaMart.Catalog.Api.csproj,                  dll: LankaMart.Catalog.Api.dll }
          - { name: inventory,     project: src/services/inventory/LankaMart.Inventory.Api/LankaMart.Inventory.Api.csproj,            dll: LankaMart.Inventory.Api.dll }
          - { name: orders,        project: src/services/orders/LankaMart.Orders.Api/LankaMart.Orders.Api.csproj,                     dll: LankaMart.Orders.Api.dll }
          - { name: payments,      project: src/services/payments/LankaMart.Payments.Api/LankaMart.Payments.Api.csproj,               dll: LankaMart.Payments.Api.dll }
          - { name: notifications, project: src/services/notifications/LankaMart.Notifications.Api/LankaMart.Notifications.Api.csproj, dll: LankaMart.Notifications.Api.dll }
          - { name: recommender,   python: true }
    steps:
      - uses: actions/checkout@v4
      - name: Lower-case the owner (GHCR requires it)
        run: echo "OWNER=${GITHUB_REPOSITORY_OWNER,,}" >> "$GITHUB_ENV"
      - uses: docker/setup-buildx-action@v3
      - uses: docker/login-action@v3
        with: { registry: ghcr.io, username: '${{ github.actor }}', password: '${{ secrets.GITHUB_TOKEN }}' }
      - name: Build & push (.NET)
        if: ${{ !matrix.python }}
        uses: docker/build-push-action@v6
        with:
          context: .
          file: infra/docker/dotnet.Dockerfile
          build-args: |
            PROJECT=${{ matrix.project }}
            DLL=${{ matrix.dll }}
          push: true
          tags: ghcr.io/${{ env.OWNER }}/lankamart-${{ matrix.name }}:${{ github.sha }}
          cache-from: type=gha,scope=${{ matrix.name }}
          cache-to: type=gha,mode=max,scope=${{ matrix.name }}
      - name: Build & push (Python)
        if: ${{ matrix.python }}
        uses: docker/build-push-action@v6
        with:
          context: src/ml/recommender
          push: true
          tags: ghcr.io/${{ env.OWNER }}/lankamart-${{ matrix.name }}:${{ github.sha }}
      - name: Vulnerability scan (fail on HIGH/CRITICAL)
        # PIN this action to a release tag you have verified (or a full commit SHA) — never a moving branch
        uses: aquasecurity/trivy-action@<PINNED-TAG-OR-SHA>
        with:
          image-ref: ghcr.io/${{ env.OWNER }}/lankamart-${{ matrix.name }}:${{ github.sha }}
          severity: HIGH,CRITICAL
          exit-code: '1'
          ignore-unfixed: true

  update-manifests:
    needs: images                                  # only runs if EVERY image built AND passed the scan
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Point the values files at the new tag
        run: |
          OWNER="${GITHUB_REPOSITORY_OWNER,,}"
          for f in deploy/helm/values/*.yaml; do
            n="$(basename "$f" .yaml)"; [ "$n" = common ] && continue
            REPO="ghcr.io/${OWNER}/lankamart-${n}" TAG="${GITHUB_SHA}" yq -i '.image.repository = strenv(REPO) | .image.tag = strenv(TAG)' "$f"
          done
      - name: Commit (Argo CD sees the commit and deploys)
        run: |
          git config user.name  "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add deploy/helm/values
          git diff --cached --quiet || (git commit -m "chore(deploy): release ${GITHUB_SHA::7} [skip ci]" && git push)
```
🧠 **Read the pipeline as a story:** merge to `main` → build one image per service tagged with the **commit SHA** (immutable; never deploy `latest`) → scan → *if all clean* → write the SHA into the values files → Argo CD notices the Git change → rolls out (migration Job first). **Git history *is* the deployment history.** *(Branch protection on `main` will reject the bot's push — either allow `github-actions[bot]` to bypass, or point Argo CD at a separate `deploy` branch the bot owns. A third-party action that is **not pinned** can be hijacked: pin every non-GitHub action by SHA once you've reviewed it.)*

One-time setup: GitHub → repo **Settings → Actions → General → Workflow permissions: Read and write**. Make the container packages visible to the cluster: create a fine-grained PAT with `read:packages` and run `GHCR_USER=<you> GHCR_TOKEN=<pat> ./scripts/k8s-secrets.sh` (creates `ghcr-pull`).

✅ **Check 5c**
1. Push a trivial code change to `main` → the **release** workflow builds nine images, scans, commits a `chore(deploy): release abc1234` commit.
2. Argo CD picks it up → pods roll to the new image (`kubectl -n lm-staging get deploy catalog -o jsonpath='{.spec.template.spec.containers[0].image}'` shows the new SHA).
3. **Break the scan on purpose:** temporarily base an image on an old, vulnerable tag → the workflow fails at *Vulnerability scan* and **nothing deploys**. Revert.
4. Revert the release commit in Git → the previous version is back. *Rollback in one command.*
5. Add a **canary-style safety check** to your habit: before merging, `helm template` the changed chart and read the diff.

Commit: `ci: release pipeline (build, scan, gitops commit)`. **ADR-022** (*GitOps with Argo CD; SHA-tagged images; scan gate; bot commits values*).

---

# PART H — SPRINT 6: LOAD TESTING WITH k6 + HPA TUNING (Week 16)

🎯 **Goal:** *measure* instead of guess. Prove the flash-sale path is **fast enough and, above all, correct under pressure** (no overselling, no lost events, balanced ledger), and tune autoscaling from evidence.

🧠 **Be honest about what a laptop can prove.** The roadmap's headline target — *10,000 concurrent buyers on one variant* — needs production-class hardware. On your machine you can prove **(1) correctness under contention, (2) the shape of the curve, (3) where the first bottleneck is**, and **(4) that autoscaling reacts**. Numbers are for *comparing before/after on the same setup*, not for bragging. A load test that only reports speed and never checks correctness is a toy; ours checks both.

**Tool:** **k6** (scripts in JavaScript, runs from the CLI; `k6 run script.js` exits non-zero when a *threshold* fails, so it works in CI). Install: `brew install k6` · `choco install k6` · Debian/Ubuntu: follow k6's apt instructions · or Docker: `docker run --rm -i --network host grafana/k6 run - < script.js`.

### 6.1 Make staging testable (temporarily)

Three guard-rails from Phase 1 will — correctly — throttle a load test that comes from one IP with a few hundred users. Make the limits **configurable**, then raise them *only for test runs*.

**(a) Gateway rate limits from config** (replace the two literals in `Program.cs`):
```csharp
var authLimit   = cfg.GetValue("RateLimit:Auth:PermitLimit", 10);
var globalLimit = cfg.GetValue("RateLimit:Global:PermitLimit", 300);
// … use globalLimit in the GlobalLimiter's PermitLimit and authLimit in the "auth" policy
```
**(b) Connection-pool arithmetic — the classic scaling trap.** Npgsql's pool defaults to **100** connections *per process*. 6 replicas × 100 = 600 possible connections against Postgres's default `max_connections=100` → under load, services fail with *"too many clients"*. Fix both sides:
* in the secrets script, append `;Maximum Pool Size=20;Minimum Pool Size=1` to every `conn-*` connection string;
* in `postgres.yaml`, give the container `args: ["postgres", "-c", "max_connections=300"]`.
*Rule: Σ(replicas × pool size) across services ≤ ~80 % of `max_connections`.* (Real systems add a pooler such as PgBouncer — Phase 6/7.)

**(c) Load-test overrides** — add to `common.yaml` **only while testing** (commit → Argo applies; revert afterwards — GitOps makes both visible in history):
```yaml
env:
  RateLimit__Global__PermitLimit: "1000000"
  RateLimit__Auth__PermitLimit: "1000000"
```
and in `identity.yaml`: `Jwt__AccessTokenMinutes: "120"` (tokens must outlive the test); in `orders.yaml`: `Orders__PaymentWindowMinutes: "2"` and `Orders__ReservationTtlMinutes: "4"` (unpaid load-test orders expire quickly and release stock).

### 6.2 Prepare users and the flash-sale product

`scripts/loadtest_prepare.py` — creates 300 verified customers (with an address) and one product with **exactly 500** units:
```python
#!/usr/bin/env python3
"""python scripts/loadtest_prepare.py  (BASE=http://localhost:8080/api/v1)   → writes loadtests/tokens.json and loadtests/flash.json"""
import json, os, time, urllib.parse, concurrent.futures as cf
import requests

BASE = os.getenv("BASE", "http://localhost:8080/api/v1"); N = int(os.getenv("USERS", "300")); PW = "Sup3rSecret!!"

def call(m, p, token=None, **kw):
    h = kw.pop("headers", {}); h.update({"Authorization": f"Bearer {token}"} if token else {})
    return requests.request(m, BASE + p, headers=h, timeout=30, **kw)

def make_user(i):
    email, phone = f"load{i:04d}@lankamart.local", f"07720{i:05d}"
    r = call("POST", "/auth/register", json=dict(email=email, phone=phone, password=PW, fullName=f"Load User {i}", role="customer"))
    if r.status_code == 202:
        for ch, dest in (("email", email), ("phone", "+94" + phone[1:])):
            code = call("GET", "/dev/otp/" + urllib.parse.quote(dest, safe="")).json()["code"]
            call("POST", "/auth/verify-otp", json=dict(email=email, channel=ch, code=code)).raise_for_status()
    tok = call("POST", "/auth/login", json=dict(email=email, password=PW)).json()["accessToken"]
    for _ in range(40):                                              # profile appears asynchronously
        if call("GET", "/customers/me", tok).status_code == 200: break
        time.sleep(0.5)
    if not call("GET", "/customers/me/addresses", tok).json():
        call("POST", "/customers/me/addresses", tok, json=dict(label="Home", recipientName=f"Load User {i}", phone=phone, line1="1 Test Road", city="Colombo", district="Colombo", postalCode="00300", isDefault=True))
    return {"email": email, "token": tok}

with cf.ThreadPoolExecutor(10) as ex: users = list(ex.map(make_user, range(N)))
os.makedirs("loadtests", exist_ok=True); json.dump(users, open("loadtests/tokens.json", "w"))

seller = call("POST", "/auth/login", json=dict(email="seller1@lankamart.local", password=PW)).json()["accessToken"]
cat = call("GET", "/categories").json()[0]["id"]
r = call("POST", "/seller/products", seller, json=dict(categoryId=cat, brand="FlashCo", translations=[dict(lang="en", name="FLASH SALE Sneakers", description="Limited drop")],
        variants=[dict(sku=f"FLASH-{os.urandom(3).hex()}", priceCents=100000)], images=[dict(url="https://picsum.photos/seed/flash/600/600")]))
r.raise_for_status(); pid, vid = r.json()["id"], r.json()["variantIds"][0]
call("POST", f"/seller/products/{pid}/publish", seller).raise_for_status()
for _ in range(30):                                                  # wait for the VariantCreated event → inventory row
    if call("POST", "/seller/inventory/adjust", seller, json=dict(variantId=vid, delta=450, reason="flash sale stock")).status_code == 204: break
    time.sleep(1)                                                    # default dev stock is 50 → +450 = exactly 500
json.dump({"variantId": vid, "stock": 500}, open("loadtests/flash.json", "w"))
print(f"{len(users)} users, flash variant {vid} with 500 units")
```
*(Assumes `Inventory__DevDefaultStock=50`. Adjust the delta if yours differs, and verify `GET /seller/inventory` shows 500 before testing.)*

### 6.3 The k6 scripts

`loadtests/smoke.js` — *"is anything on fire?"* (run first, always)
```js
import http from 'k6/http';
import { check } from 'k6';
export const options = { vus: 1, duration: '30s', thresholds: { http_req_failed: ['rate<0.01'], http_req_duration: ['p(95)<500'] } };
const BASE = __ENV.BASE_URL || 'http://localhost:8080/api/v1';
export default function () {
  for (const p of ['/categories', '/products?page_size=12', '/products?query=shoe']) check(http.get(BASE + p), { [`${p} 200`]: (r) => r.status === 200 });
}
```
`loadtests/browse.js` — the read path: *arrival-rate* executor (fixed requests/second regardless of response time — the honest way to test capacity, unlike fixed VUs which slow down as the server does)
```js
import http from 'k6/http';
import { check } from 'k6';
export const options = {
  scenarios: { browse: { executor: 'ramping-arrival-rate', startRate: 20, timeUnit: '1s', preAllocatedVUs: 100, maxVUs: 500,
    stages: [{ target: 100, duration: '1m' }, { target: 250, duration: '3m' }, { target: 0, duration: '1m' }] } },
  thresholds: { http_req_failed: ['rate<0.01'], 'http_req_duration{kind:search}': ['p(95)<400', 'p(99)<800'], 'http_req_duration{kind:detail}': ['p(95)<300'] },
};
const BASE = __ENV.BASE_URL || 'http://localhost:8080/api/v1';
const WORDS = ['shoe', 'phone', 'tea', 'rice', 'bottle', 'mat', 'pen', 'bag', 'cooker', 'serum'];
export default function () {
  const q = WORDS[Math.floor(Math.random() * WORDS.length)];
  const s = http.get(`${BASE}/products?query=${q}&page_size=12`, { tags: { kind: 'search' } });
  check(s, { 'search 200': (r) => r.status === 200 });
  const items = s.status === 200 ? s.json('items') : [];
  if (items && items.length) check(http.get(`${BASE}/products/${items[0].slug}`, { tags: { kind: 'detail' } }), { 'detail 200': (r) => r.status === 200 });
}
```
`loadtests/flash-sale.js` — **the star**: hundreds of users fight for 500 units
```js
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';

const users = JSON.parse(open('./tokens.json'));       // init-time file read
const flash = JSON.parse(open('./flash.json'));
const BASE = __ENV.BASE_URL || 'http://localhost:8080/api/v1';

const sold = new Counter('orders_created');            // 201
const soldOut = new Counter('sold_out');               // 409 OUT_OF_STOCK  (correct behaviour, not an error!)
const unexpected = new Counter('unexpected_errors');   // anything else

export const options = {
  scenarios: { flash: { executor: 'ramping-vus', startVUs: 0, gracefulRampDown: '10s',
    stages: [{ duration: '30s', target: 200 }, { duration: '1m', target: 500 }, { duration: '30s', target: 0 }] } },
  thresholds: {
    orders_created: ['count<=500'],                    // ★ THE OVERSELL CHECK: never more successful orders than units in stock
    unexpected_errors: ['count<5'],
    'http_req_duration{name:place_order}': ['p(95)<1500', 'p(99)<3000'],
  },
};

const guid = () => Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16)).join('');   // 32 hex digits = a valid GUID ("N" format)

export default function () {
  const u = users[(__VU - 1) % users.length];
  const auth = { Authorization: `Bearer ${u.token}`, 'Content-Type': 'application/json' };
  const add = http.post(`${BASE}/cart/items`, JSON.stringify({ variantId: flash.variantId, quantity: 1 }), { headers: auth, tags: { name: 'cart_add' } });
  if (add.status !== 204) { unexpected.add(1); return; }

  const order = http.post(`${BASE}/orders`, JSON.stringify({ deliveryAddress: { recipientName: 'Load User', phone: '0772000000', line1: '1 Test Road', city: 'Colombo', district: 'Colombo', postalCode: '00300' } }),
    { headers: { ...auth, 'Idempotency-Key': guid() }, tags: { name: 'place_order' } });

  if (order.status === 201) sold.add(1);
  else if (order.status === 409 && order.json('code') === 'OUT_OF_STOCK') { soldOut.add(1); sleep(1); }   // sold out: back off, like a real user would
  else if (order.status === 409 && order.json('code') === 'ITEM_UNAVAILABLE') { soldOut.add(1); sleep(1); }
  else unexpected.add(1);
  sleep(0.2);
}
```
`loadtests/checkout-e2e.js` (smaller, 30 VUs): the **whole** purchase — add to cart → order → `POST /payments/{id}/pay` with card `4111111111111111` → poll `GET /payments/{id}` until `Succeeded` → assert the order is `Paid`. Thresholds: `p(95)` of a custom `Trend` (`checkout_total_ms`) under ~8 s (the mock adds ~2 s) and `checks > 99%`. Write it by combining the pieces above — it exercises the payment webhook, outbox, consumers and inventory confirm **concurrently**.

### 6.4 Run, then **verify correctness** (the part most people skip)

```bash
python scripts/loadtest_prepare.py
k6 run loadtests/smoke.js                     # 1) sanity
k6 run loadtests/browse.js                    # 2) read path capacity
k6 run loadtests/flash-sale.js                # 3) contention
k6 run loadtests/checkout-e2e.js              # 4) full flow
python scripts/verify_after_load.py           # 5) INVARIANTS — speed means nothing without these
```
`scripts/verify_after_load.py` — runs SQL/Redis checks inside the cluster (as the Postgres superuser) and prints PASS/FAIL:
```python
#!/usr/bin/env python3
import json, subprocess, sys, time
NS = "lm-staging"; flash = json.load(open("loadtests/flash.json")); vid = flash["variantId"]; stock = flash["stock"]
def sh(*a): return subprocess.run(a, capture_output=True, text=True, check=True).stdout.strip()
def sql(db, q): return sh("kubectl", "-n", NS, "exec", "postgres-0", "--", "psql", "-U", "postgres", "-d", db, "-Atc", q)
def redis(*a): return sh("kubectl", "-n", NS, "exec", "redis-0", "--", "redis-cli", *a)
ok = True
def check(name, cond, detail=""):
    global ok; ok &= bool(cond); print(("PASS " if cond else "FAIL ") + name, detail)

on_hand, reserved = map(int, sql("inventory_db", f"SELECT qty_on_hand, qty_reserved FROM inventory.inventory_items WHERE variant_id='{vid}'").split("|"))
active_orders = int(sql("orders_db", f"SELECT coalesce(sum(oi.quantity),0) FROM orders.order_items oi JOIN orders.orders o ON o.id=oi.order_id WHERE oi.variant_id='{vid}' AND o.status IN ('PendingPayment','Paid')"))
paid_units = int(sql("orders_db", f"SELECT coalesce(sum(oi.quantity),0) FROM orders.order_items oi JOIN orders.orders o ON o.id=oi.order_id WHERE oi.variant_id='{vid}' AND o.status='Paid'"))
redis_stock = int(redis("GET", f"stock:{vid}") or 0)

check("never oversold: units ordered ≤ stock", active_orders <= stock, f"({active_orders} ≤ {stock})")
check("stock never negative", on_hand >= 0 and on_hand - reserved >= 0, f"(on_hand={on_hand}, reserved={reserved})")
check("postgres: on_hand == original stock − paid units", on_hand == stock - paid_units, f"(on_hand={on_hand}, paid={paid_units})")
check("redis counter == postgres available", redis_stock == on_hand - reserved, f"(redis={redis_stock}, pg={on_hand - reserved})")
check("reservations table agrees with qty_reserved", int(sql("inventory_db", f"SELECT coalesce(sum(l.quantity),0) FROM inventory.reservation_lines l JOIN inventory.reservations r ON r.id=l.reservation_id WHERE r.status='Reserved' AND l.variant_id='{vid}'")) == reserved)
check("ledger balanced", sql("payments_db", "SELECT coalesce(sum(debit_cents)-sum(credit_cents),0) FROM payments.ledger_entries") == "0")
for db, schema in (("identity_db","identity"),("users_db","users"),("catalog_db","catalog"),("inventory_db","inventory"),("orders_db","orders"),("payments_db","payments"),("notifications_db","notifications")):
    backlog = int(sql(db, f"SELECT count(*) FROM {schema}.outbox_message"))
    check(f"outbox drained in {schema}", backlog == 0, f"(backlog={backlog})")
sys.exit(0 if ok else 1)
```
Run it once right after the test (reservations still held → invariants 1–5 hold) and again after the payment window + TTL elapsed (≈ 5 min): `qty_reserved` returns to **0** and Redis returns to the full stock — proof the sweepers and compensation work at volume.

✅ **Check**
* `smoke.js` and `browse.js` pass their thresholds (or you know exactly which fail and why).
* **`flash-sale.js`: `orders_created` ≤ 500 — in practice exactly 500 — and `verify_after_load.py` prints all PASS.** Run it **three times** (re-run `loadtest_prepare.py` for fresh stock each time). One overselling run in three = a real race condition: stop and debug (compare Redis/Postgres, read `STOCK DRIFT` logs, look at traces in Jaeger if enabled).
* RabbitMQ UI: no queue growing without bound during the test; `_error` queues empty.
* Outbox backlog returns to 0 within seconds after the test.

### 6.5 Tune autoscaling from evidence

```bash
kubectl -n lm-staging get hpa -w            # terminal 1: targets and replica counts
kubectl -n lm-staging top pods              # terminal 2: CPU/memory per pod (repeat every few seconds)
k6 run loadtests/flash-sale.js              # terminal 3: the load
```
**The investigation order** (find the *first* bottleneck, fix it, re-measure — never tune everything at once):
1. **Which service saturates first?** (`top pods`) Usually Orders or the Gateway.
2. **Is Postgres the limit?** CPU on `postgres-0`, `SELECT count(*) FROM pg_stat_activity;` vs `max_connections`. *"too many clients"* ⇒ the pool arithmetic (6.1b).
3. **RabbitMQ:** queue depth growing ⇒ a consumer is too slow (scale that service or batch).
4. **Redis:** `redis-cli --latency`; Lua scripts are fast, but one huge hot key is a single-threaded limit (that is the architecture's honest ceiling per product).
5. **Latency vs. errors:** rising p99 with zero errors = queueing (add replicas); errors = a limit or bug.

**Knobs and what they really do**
| Knob | Effect / rule |
|---|---|
| `resources.requests.cpu` | HPA percentages are **relative to requests**. Measure steady-state CPU per pod under load; set requests ≈ that / 0.7. Too low ⇒ HPA scales constantly; too high ⇒ never scales and wastes node capacity. |
| `hpa.targetCPU` | 70 = steady; 50–60 for spiky traffic (more headroom, earlier scale-out). |
| `hpa.minReplicas` | HPA reacts in ~15–30 s and a new pod needs ~10–20 s to become Ready — **too slow for a flash-sale spike**. For a *scheduled* sale, raise `minReplicas` an hour before (a one-line Git commit) and lower it after. |
| `hpa.maxReplicas` | cap by DB connections: `maxReplicas × pool size` must still fit `max_connections`. |
| Memory limit | .NET respects it; OOMKilled pods = limit too low (`kubectl describe pod`). No CPU **limit** (throttling adds latency). |
| `PodDisruptionBudget` | keep ≥ 1 so a node drain never takes the last pod. |

Re-run the same test after each change and record results in **`docs/load-test-results.md`**:

| Date | Scenario | Load | p95 / p99 | Errors | Replicas (peak) | First bottleneck | Change made |
|---|---|---|---|---|---|---|---|

**Draft your SLOs** (they become alerts in Phase 6): browse p99 < 800 ms · checkout availability 99.9 % · **0 oversold units, ledger always balanced** (invariants, not percentages) · outbox lag < 30 s.

⚠️ **Pitfalls:** testing from the same machine that runs the cluster (the load generator steals CPU — note it in your results) · using *fixed-VU* mode for capacity (it hides overload) · counting `409 OUT_OF_STOCK` as errors (it's the **correct** answer once stock is gone) · leaving the raised rate limits / 120-minute tokens in place after the test — **revert them** (the PR diff shows exactly what you changed) · judging by averages · ignoring warm-up (JIT: discard the first 30 s when comparing).

Commit: `test(load): k6 scripts, verification, hpa tuning`. **ADR-023** (*k6 as load tool; invariants verified after every run*), **ADR-024** (*connection-pool budget rule*).

---

# PART I — PHASE 2 GATE, TROUBLESHOOTING, AND WHAT'S NEXT

## I.1 Gate — definition of done

**Data & messaging**
- [ ] Six (seven with Notifications) separate databases; each login **cannot connect** to the others (`permission denied for database`)
- [ ] Migrations run as `--migrate-only` Jobs; no service auto-migrates outside Development
- [ ] Outbox + inbox in **every** service; **broker-down test** passes (writes succeed, events drain after recovery); duplicate-delivery test passes
- [ ] No `Publish` remains without a matching `SaveChanges`/`InTransactionAsync` (code-review grep: `grep -rn "\.Publish(" src/services`)

**Features**
- [ ] OTPs and order/seller events arrive as real emails (Mailpit), in-app inbox, preferences respected; retries and dead-letter proven
- [ ] Reconciler detects and heals induced drift; alert threshold fires; workers switchable

**Platform**
- [ ] k3s cluster runs every service from the generic Helm chart: probes, rolling updates (zero failed requests during a deploy), HPA, PDB, non-root read-only containers, default-deny **NetworkPolicies** (proven with the probe pod)
- [ ] **Argo CD** deploys from Git; self-heal and rollback-by-revert demonstrated
- [ ] CI release pipeline: build → scan (fails on HIGH/CRITICAL) → SHA-tagged images → values commit → Argo deploys
- [ ] **Gate (roadmap):** staging deploys via GitOps ✔ · **flash-sale load test passes** (`orders_created ≤ stock`, all invariants PASS, 3 runs in a row) ✔ · services independently deployable (redeploy only Catalog and watch nothing else restart) ✔

**Quality**
- [ ] Tests added per §Sprint checks; CI (dotnet + web + python) still green; `docs/load-test-results.md`, ADR-017…024, updated known-gaps committed
- [ ] `git tag phase-2-complete && git push --tags`

**10-minute demo script:** (1) `git push` a small change → watch Actions → Argo CD roll it out, no failed requests · (2) stop RabbitMQ, place an order, restart → event delivered (outbox) · (3) buy something → Mailpit email + React bell · (4) induce Redis drift → reconciler heals · (5) run the flash-sale test live: `kubectl get hpa -w` beside k6 · (6) show the verify script all-PASS and the balanced ledger.

**Self-quiz**
1. What exact failure does the outbox prevent, and what does the inbox add on top?
2. Why does `InTransactionAsync` exist when `SaveChanges` is already transactional?
3. Why is the OTP sent by a synchronous internal call and not by an event?
4. What does `FOR UPDATE SKIP LOCKED` + a lease give you that a simple `UPDATE … WHERE status='Pending'` does not?
5. Why must the reconciler double-check and use compare-and-set?
6. Difference between liveness, readiness and startup probes — what happens when Postgres goes down?
7. Why `maxUnavailable: 0` in the rolling update?
8. What does default-deny buy you if every pod is already authenticated by JWT?
9. Why is the migration a PreSync Job instead of code at app start?
10. Why can raising `maxReplicas` *cause* an outage? (connection pools)
11. Why SHA tags instead of `latest`?
12. Why is "0 oversold units" checked by a script and not just by k6's own pass/fail?

## I.2 Troubleshooting (Phase 2)

| Symptom | Likely cause → fix |
|---|---|
| Events never leave a service | forgot `SaveChanges` after `Publish`, or `UseBusOutbox` missing/`AddOutbox` migration not applied → check `<schema>.outbox_message` is empty *and* the handler's DB write happened |
| `outbox_message` grows forever | RabbitMQ unreachable/wrong credentials, or delivery service not running (check logs for MassTransit connection errors) |
| Consumers throw `40001` serialization failure under load | strict isolation of the consumer outbox; retries usually absorb it — if persistent, read MassTransit's outbox docs on isolation level/locking |
| Notification stuck `Sending` | worker crashed mid-send: wait for the 2-min lease to expire, it's re-claimed automatically |
| Email "sent" but nothing in Mailpit | Notification points at the wrong SMTP host (`mailpit` in-cluster vs `localhost:1025` locally) |
| `helm template` / install fails with YAML error | indentation inside `nindent`/`include` — render with `helm template` and find the line; run `helm lint` |
| Pod `CrashLoopBackOff` | `kubectl logs <pod> --previous`: usually missing config (`Auth` section), wrong connection string secret, or Postgres not ready |
| Migration Job fails | wrong DB secret / DB not ready (retries 6×) / a bad migration — read `kubectl logs job/<svc>-migrate`; Argo shows it as a failed hook and **blocks the rollout** (old version keeps serving — that's the safety) |
| Argo app `OutOfSync` forever | a field mutated by the cluster (e.g., HPA-managed `replicas`): the chart omits `replicas` when HPA is enabled; or add `ignoreDifferences` |
| Argo can't read the repo | repository credential missing/expired in Argo CD settings |
| Release workflow can't push the values commit | branch protection blocks the bot; allow bypass or use a deploy branch |
| `ImagePullBackOff` in staging | package visibility/`ghcr-pull` secret; image tag not yet built |
| Everything slow after enabling NetworkPolicies | DNS egress rule missing (`allow-dns`) or a service lost its `part-of: lankamart` label |
| k6: many `429` | rate limits still at Phase 1 values (6.1a/c) |
| k6: many `5xx` and "too many clients" in Postgres logs | connection-pool arithmetic (6.1b) |
| Flash-sale test shows `orders_created` > stock | **real bug** — capture `verify_after_load.py` output, Redis vs Postgres values, and the reservation rows; do not tune anything until explained |

## I.3 Updated known-gaps register (carry into `docs/known-gaps.md`)

| # | Gap | Status after Phase 2 |
|---|---|---|
| 1 | Dual write | ✅ **closed** (outbox/inbox) |
| 2 | OTPs only logged | ✅ **closed** (Notification service) |
| 3 | Shared internal API key | open → Phase 6 (mTLS/mesh) |
| 4 | Web refresh token in `localStorage` | open → Phase 6 |
| 5 | Mock webhook jobs in memory | n/a (mock only) |
| 6 | Redis↔Postgres drift | ✅ **closed** (reconciler + alert) |
| 7 | KYC PII in clear text | open → Phase 3/6 |
| 8 | No MFA for sellers/admins | open → Phase 3/6 |
| 9 | Client IP via `X-Forwarded-For` spoofable | partly mitigated (NetworkPolicy: only gateway reaches services); full fix Phase 6 |
| 10 | Shipments mirror the order | open → Phase 4 |
| 11 | Single currency, flat fee | open |
| 12 | Auto-migrate races | ✅ **closed** (migration Jobs) |
| 13 | Revoke by user not possible | open → Phase 6 |
| 14 | Staging data stores are single-replica StatefulSets | new → Phase 6/7 (managed/operator, backups, PITR) |
| 15 | No metrics/alerts stack (Prometheus/Grafana/Loki) | new → Phase 6 |
| 16 | Secrets are plain Kubernetes Secrets created by script | new → Phase 6 (Sealed Secrets/SOPS/Vault + rotation) |
| 17 | Ingress API (frozen) / Traefik; no TLS | new → Phase 6/7 (Gateway API, cert-manager, TLS) |
| 18 | MassTransit v8 maintenance ends after 2026 | open → decide before launch (ADR-003) |
| 19 | No PgBouncer; per-pod pools sized by hand | new → Phase 6/7 |
| 20 | Staff notifications (KYC queue, DLQ alerts) missing | new → Phase 3 (admin panel) |

## I.4 What comes next — Phase 3 preview (Weeks 17–26: Marketplace Depth)
Seller portal (React) · KYC document uploads with the **Media service** (object storage: pick **SeaweedFS or Garage** — MinIO is archived — plus presigned uploads, ClamAV, EXIF stripping) · product moderation queue · **Localization service** + trilingual UI, search with **Meilisearch** multilingual analyzers and the Notification templates moving to localized content · reviews (verified purchase) · SEO (SSR storefront, sitemap, JSON-LD) · seller MFA. Your Phase 2 platform now makes each of those a **new service deployed by `git push`**.

> **Final word.** Phase 1 taught you to make a system *correct*. Phase 2 taught you to make it *durable* (outbox), *operable* (workers, reconciler), *deployable* (GitOps) and *measurable* (k6 with invariants). The habits you've built — measure first, verify invariants, make every change a reviewed commit — are the ones that scale with the system.

---

# APPENDICES

## Appendix A — Ports, URLs, and credentials added in Phase 2

| Component | Where |
|---|---|
| Notifications service | `:5107` locally · `http://notifications` in-cluster |
| Mailpit (fake email inbox) | SMTP `:1025`, UI `http://localhost:8025` |
| k3d cluster ingress (Traefik → gateway) | `http://localhost:8080` |
| Argo CD UI | `kubectl -n argocd port-forward svc/argocd-server 8081:443` → `https://localhost:8081` (`admin`, password from `argocd-initial-admin-secret`) |
| Postgres (local compose) | `:5432`; databases `identity_db … notifications_db`, logins `<svc>_svc / <svc>_dev_pw` |
| Staging secrets | created by `scripts/k8s-secrets.sh`; **not in Git** |

## Appendix B — New configuration keys

| Key | Meaning | Default |
|---|---|---|
| `Database:AutoMigrate` | apply migrations at startup | `true` in Development, else `false` |
| `--migrate-only` (CLI arg) | migrate then exit (Kubernetes/Argo Job) | — |
| `Workers:Enabled` | run background loops in this process | `true` |
| `Features:DevTools` | dev/staging tools (OTP lookup, simulator, failure lab, seeded admins). **Startup refuses it in Production** | `false` |
| `Otp:Sender` | `log` or `notification` | `log` |
| `Email:*`, `Sms:*`, `Push:*` | SMTP settings; SMS/push provider selection | see §3.1 |
| `Delivery:{PollSeconds,BatchSize,MaxAttempts}` | notification delivery worker | 5 / 20 / 5 |
| `Reconciler:{EveryMinutes,AutoHeal}` | stock reconciler | 60 / `true` |
| `RateLimit:{Global,Auth}:PermitLimit` | gateway limits per IP per minute | 300 / 10 |
| `Services:Notifications` | URL used by Identity for OTP | — |
| `Jwt:AccessTokenMinutes` | access-token lifetime | 15 |

## Appendix C — Endpoints added

`GET /api/v1/me/notifications[?unread=true]` · `GET …/unread-count` · `POST …/{id}/read` · `POST …/read-all` · `GET/PUT /api/v1/me/notification-preferences` · `POST /api/v1/me/devices` · `GET /api/v1/admin/notifications/dead-letter` · `POST …/{id}/requeue` (customer_manager, super_admin) · **internal:** `POST /api/v1/internal/notifications/otp` · `POST /api/v1/internal/inventory/reconcile`.

## Appendix D — Event consumers added

| Consumer (Notifications) | Event | Channels |
|---|---|---|
| `UserRegisteredConsumer` | `UserRegistered` | builds contact read-model; welcome email + in-app |
| `OrderConsumers` | `OrderPaid` · `OrderStatusChanged` · `OrderCancelled` | email/in-app/push · in-app/push · in-app |
| `PaymentFailedConsumer` | `PaymentFailed` | in-app |
| `SellerConsumers` | `SellerApproved` · `SellerRejected` · `InventoryLowStock` | email + in-app · email + in-app · in-app |

## Appendix E — Command cheat-sheet (Kubernetes, Helm, Argo, k6)

```bash
# ---- kubectl (add -n lm-staging) ----
kubectl get pods|deploy|svc|hpa|ing|jobs|netpol          # list things
kubectl describe pod <p>                                  # events at the bottom explain most failures
kubectl logs <pod> [-f] [--previous] ; kubectl logs deploy/orders
kubectl exec -it deploy/catalog -- sh                     # a shell (read-only FS; /tmp is writable)
kubectl port-forward svc/mailpit 8025:8025
kubectl rollout status|history|undo deployment/orders
kubectl scale statefulset postgres --replicas=0           # outage drill
kubectl top pods|nodes                                    # needs metrics-server (bundled in k3s)
kubectl get events --sort-by=.lastTimestamp
# ---- helm ----
helm lint deploy/helm/service ; helm template catalog deploy/helm/service -f deploy/helm/values/common.yaml -f deploy/helm/values/catalog.yaml
helm upgrade --install <name> deploy/helm/service -n lm-staging -f … ; helm list -n lm-staging ; helm rollback <name> 1
# ---- k3d ----
k3d cluster create|delete|list lankamart ; k3d image import <img> -c lankamart
# ---- k6 ----
k6 run loadtests/smoke.js ; k6 run -e BASE_URL=http://localhost:8080/api/v1 loadtests/browse.js
# ---- Postgres in the cluster ----
kubectl -n lm-staging exec -it postgres-0 -- psql -U postgres -d orders_db
```

## Appendix F — Phase 2 decision log (write as ADRs 017–024; 025–026 reserved)

| ADR | Decision | One-line reason |
|---|---|---|
| 017 | Database per service via init script; migrations as `--migrate-only` jobs | isolation + safe multi-replica rollouts |
| 018 | Transactional outbox/inbox (MassTransit EF, generic registration) | no lost/duplicate events; compile-time enforcement |
| 019 | Central Notification service; OTP via synchronous internal call; SMTP adapter | secrets off the bus; vendor-neutral |
| 020 | Reconcile Redis toward Postgres with double-check + CAS; separable workers | safe self-healing; scale API and loops independently |
| 021 | k3s + Traefik; one generic Helm chart; default-deny NetworkPolicies | simplicity + least privilege (ingress-nginx retired) |
| 022 | GitOps (Argo CD), SHA-tagged images, scan gate, bot commits values | Git is the deployment history; rollback = revert |
| 023 | k6 + post-run invariant verification | correctness, not just speed |
| 024 | Connection-pool budget rule (Σ replicas × pool ≤ 80 % max_connections) | autoscaling must not exhaust the database |

**Glossary additions:** **Argo CD** GitOps controller · **ApplicationSet** generates many Argo apps from one template · **Arrival-rate executor** (k6) fixed requests/s · **CAS** compare-and-set · **GitOps** Git as source of truth for deployments · **HPA** horizontal pod autoscaler · **Inbox** processed-message ledger · **k3s/k3d** lightweight Kubernetes / k3s in Docker · **Lease** time-limited claim on work · **NetworkPolicy** pod firewall · **Outbox** events stored with the data and published later · **PDB** pod disruption budget · **PreSync hook** job run before a deployment · **Probe** liveness/readiness/startup health check · **SKIP LOCKED** Postgres feature for competing workers · **SLO** service-level objective.

---

# CONCLUSION OF PHASE 2

You now run a **durable, observable-enough, automatically deployed** marketplace core: one database per service, events that cannot be lost or applied twice, real notifications, self-healing stock, a Kubernetes cluster that heals and rolls out without dropping requests, a pipeline where **`git push` is a deployment**, and a load test that proves the most dangerous path — selling the last unit — stays correct under pressure.

**First action:** tick the Gate in §I.1 from the top; don't start Phase 3 with any box open. Then write `docs/retro-phase-2.md` — what surprised you, what you'd estimate differently, and which gap in §I.3 worries you most. That answer is your Phase 6 priority list.

— *End of the Phase 2 guide* —