// LankaMart dev runner (replaces scripts/dev.py from the build guide — see docs/adr/0017-aspire-dev-runner.md).
//
// Ports are FIXED to the guide's Appendix A (gateway 5100, identity 5101 … payments 5106, PG 5433 (host; 5432 is taken by a native PostgreSQL), Redis 6379, RabbitMQ 5672/15672)
// and endpoints are NOT proxied, so every appsettings.Development.json, the JWT Authority and the curl checks in
// docs/build-guide.md work the same with or without Aspire.
//
// ⚠ docker-compose.yml publishes the same host ports. Run `docker compose down` before starting the AppHost (and vice versa).

var builder = DistributedApplication.CreateBuilder(args);

// ───────────── Infrastructure ─────────────
var pgUser = builder.AddParameter("postgres-user", secret: false);
var pgPassword = builder.AddParameter("postgres-password", secret: true);
var postgres = builder.AddPostgres("postgres", pgUser, pgPassword, port: 5433)   // 5433: a native PostgreSQL already owns 5432 on this machine
    .WithImageTag("17-alpine")
    .WithEnvironment("POSTGRES_DB", "postgres")           // default admin DB; 01-databases.sh creates identity_db … notifications_db
    .WithInitFiles("../infra/postgres/init")              // one DATABASE + one login per service (shared with docker-compose.yml)
    // Phase 2 renamed the volume: init scripts only run on an EMPTY volume, so the Phase 1 one-DB volume
    // ("lankamart-aspire-pgdata") would never get the per-service databases. Delete the old one with
    // `docker volume rm lankamart-aspire-pgdata` once you no longer need its data.
    .WithDataVolume("lankamart-aspire-pgdata-p2")
    .WithLifetime(ContainerLifetime.Persistent);

// Plain Redis exactly like docker-compose.yml (no TLS, no password, AOF on), so services keep the guide's
// "localhost:6379" connection string. (Aspire's AddRedis would add TLS + a generated password.)
var redis = builder.AddContainer("redis", "redis", "7-alpine")
    .WithArgs("redis-server", "--appendonly", "yes")
    .WithEndpoint(port: 6379, targetPort: 6379, name: "tcp", scheme: "tcp", isProxied: false)
    .WithVolume("lankamart-aspire-redisdata", "/data")
    .WithLifetime(ContainerLifetime.Persistent);

var rabbitUser = builder.AddParameter("rabbitmq-user", secret: false);
var rabbitPassword = builder.AddParameter("rabbitmq-password", secret: true);
var rabbitmq = builder.AddRabbitMQ("rabbitmq", rabbitUser, rabbitPassword, port: 5672)
    .WithManagementPlugin(port: 15672)
    .WithDataVolume("lankamart-aspire-rabbitdata")
    .WithLifetime(ContainerLifetime.Persistent);

// ───────────── .NET services ─────────────
IResourceBuilder<ProjectResource> Service<TProject>(string name, bool usesRedis = false, bool usesDb = true)
    where TProject : IProjectMetadata, new()
{
    var svc = builder.AddProject<TProject>(name)
        .WithEndpoint("http", e => e.IsProxied = false)           // real fixed port from launchSettings.json
        .WithEnvironment("Otel__Endpoint", "")                     // export to the Aspire dashboard (OTEL_EXPORTER_OTLP_*) instead of Jaeger
        .WithHttpHealthCheck("/health/ready");
    if (usesDb)
    {
        svc.WaitFor(postgres).WaitFor(rabbitmq);
    }
    if (usesRedis)
    {
        svc.WaitFor(redis);   // connection string "localhost:6379" comes from the service's appsettings.Development.json
    }
    return svc;
}

var identity = Service<Projects.LankaMart_Identity_Api>("identity", usesRedis: true);
var users = Service<Projects.LankaMart_Users_Api>("users");
var catalog = Service<Projects.LankaMart_Catalog_Api>("catalog");
var inventory = Service<Projects.LankaMart_Inventory_Api>("inventory", usesRedis: true);
var orders = Service<Projects.LankaMart_Orders_Api>("orders", usesRedis: true);
var payments = Service<Projects.LankaMart_Payments_Api>("payments");

var gateway = Service<Projects.LankaMart_Gateway>("gateway", usesRedis: true, usesDb: false)
    .WaitFor(identity)
    .WithReference(users).WithReference(catalog).WithReference(inventory).WithReference(orders).WithReference(payments)
    .WithExternalHttpEndpoints();

// ───────────── Angular client (storefront comes in a later phase) ─────────────
builder.AddJavaScriptApp("storefront", "../e_com_app.client", "start")
    .WithHttpsEndpoint(port: 61501, env: "DEV_SERVER_PORT", isProxied: false)
    .WithReference(gateway)
    .WaitFor(gateway)
    .WithExternalHttpEndpoints();

builder.Build().Run();
