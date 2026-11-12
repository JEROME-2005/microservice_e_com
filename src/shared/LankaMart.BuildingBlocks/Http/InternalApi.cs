using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace LankaMart.BuildingBlocks;

/// <summary>Rejects calls that don't carry the shared internal key. Internal routes are NEVER routed by the gateway.</summary>
public sealed class InternalOnlyFilter(IConfiguration cfg) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = cfg["Internal:ApiKey"];
        var given = context.HttpContext.Request.Headers["X-Internal-Key"].ToString();
        if (string.IsNullOrEmpty(expected) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
        {
            return Results.Problem(statusCode: 401, title: "Invalid internal key",
                extensions: new Dictionary<string, object?> { ["code"] = "INTERNAL_AUTH_FAILED" });
        }
        return await next(context);
    }
}

public sealed class InternalKeyHandler(IConfiguration cfg) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("X-Internal-Key", cfg["Internal:ApiKey"]);
        return base.SendAsync(request, cancellationToken);
    }
}

public static class InternalApiExtensions
{
    /// <summary>app.MapInternalGroup("/api/v1/internal/inventory") → anonymous for JWT, but protected by the internal key.</summary>
    public static RouteGroupBuilder MapInternalGroup(this IEndpointRouteBuilder app, string prefix) =>
        app.MapGroup(prefix).AllowAnonymous().AddEndpointFilter<InternalOnlyFilter>();

    /// <summary>Typed HttpClient for calling another service: base URL from "Services:{name}", internal key, retries, circuit breaker, timeouts.</summary>
    /// <param name="resilience">Optional tweaks to the standard pipeline, e.g. a short budget for a call a user is waiting on.</param>
    public static IHttpClientBuilder AddInternalClient<TClient, TImpl>(this IServiceCollection s, IConfiguration c, string serviceName,
        Action<HttpStandardResilienceOptions>? resilience = null)
        where TClient : class where TImpl : class, TClient
    {
        s.AddTransient<InternalKeyHandler>();
        var builder = s.AddHttpClient<TClient, TImpl>(http =>
                http.BaseAddress = new Uri(c[$"Services:{serviceName}"] ?? throw new InvalidOperationException($"Missing 'Services:{serviceName}' config")))
            .AddHttpMessageHandler<InternalKeyHandler>();
        var pipeline = builder.AddStandardResilienceHandler();   // Polly v8: retry (exp. backoff + jitter) + circuit breaker + timeouts
        if (resilience is not null)
        {
            pipeline.Configure(resilience);
        }
        return builder;
    }
}
