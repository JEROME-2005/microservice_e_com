using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LankaMart.BuildingBlocks;

public static class MessagingExtensions
{
    /// <param name="services">The service collection.</param>
    /// <param name="config">Reads RabbitMq:Host/Port/Username/Password.</param>
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
