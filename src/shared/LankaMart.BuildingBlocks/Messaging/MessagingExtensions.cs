using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LankaMart.BuildingBlocks;

public static class MessagingExtensions
{
    /// <summary>MassTransit over RabbitMQ with the TRANSACTIONAL OUTBOX + INBOX on <typeparamref name="TDbContext"/> (docs/adr/0020-outbox-inbox.md).
    /// Generic on purpose: you cannot register messaging without naming the database that holds the outbox, so a
    /// "direct publish" that bypasses it is a compile error, not a code-review finding.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">Reads RabbitMq:Host/Port/Username/Password.</param>
    /// <param name="serviceName">Prefix for queue names, e.g. "catalog" → queue "catalog-seller-approved". Prevents two services sharing a queue by accident.</param>
    /// <param name="configure">Register your consumers here: x => x.AddConsumers(typeof(Program).Assembly)</param>
    public static IServiceCollection AddPlatformMessaging<TDbContext>(this IServiceCollection services, IConfiguration config,
        string serviceName, Action<IBusRegistrationConfigurator>? configure = null) where TDbContext : DbContext
    {
        services.AddMassTransit(x =>
        {
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, includeNamespace: false));
            configure?.Invoke(x);                                   // consumers
            x.AddPlatformOutbox<TDbContext>();

            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(config["RabbitMq:Host"] ?? "localhost", ushort.Parse(config["RabbitMq:Port"] ?? "5672"), "/", h =>
                {
                    h.Username(config["RabbitMq:Username"] ?? "lankamart");
                    h.Password(config["RabbitMq:Password"] ?? "lankamart_dev_pw");
                });
                // Delivery is still AT-LEAST-ONCE (broker redeliveries, outbox re-sends after a crash). The inbox makes the EFFECT
                // exactly-once; handlers stay naturally idempotent anyway (belt and braces). Transient failures are retried, then
                // the message goes to "<queue>_error" (dead-letter) where it can be inspected/replayed.
                cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
                cfg.ConfigureEndpoints(ctx);
            });
        });
        return services;
    }

    /// <summary>The outbox/inbox wiring shared by <see cref="AddPlatformMessaging{TDbContext}"/> and the integration tests
    /// (which run the same configuration on the in-memory transport).</summary>
    public static IBusRegistrationConfigurator AddPlatformOutbox<TDbContext>(this IBusRegistrationConfigurator x) where TDbContext : DbContext
    {
        x.AddEntityFrameworkOutbox<TDbContext>(o =>
        {
            o.UsePostgres();                                        // Postgres row locking → several replicas can share one outbox safely
            o.UseBusOutbox();                                       // IPublishEndpoint.Publish now WRITES AN OUTBOX ROW, committed by SaveChanges
            o.QueryDelay = TimeSpan.FromSeconds(1);                 // how often the delivery service looks for undelivered rows
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);  // how long the INBOX remembers processed message ids
        });
        // INBOX + consumer outbox: every consumer runs inside ONE database transaction together with its own changes,
        // the processed-message record and any events it publishes → exactly-once EFFECT. Throwing rolls back all three.
        x.AddConfigureEndpointsCallback((context, _, cfg) => cfg.UseEntityFrameworkOutbox<TDbContext>(context));
        return x;
    }
}
