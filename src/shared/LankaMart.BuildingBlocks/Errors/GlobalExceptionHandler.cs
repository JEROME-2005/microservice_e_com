using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LankaMart.BuildingBlocks;

/// <summary>One error format everywhere (RFC 7807 Problem Details) with a machine-readable <c>code</c> and a <c>traceId</c>.</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            AppException a => (a.Status, a.Code, a.Message),
            BadHttpRequestException b => (b.StatusCode, "BAD_REQUEST", b.Message),
            _ => (500, "INTERNAL_ERROR", "An unexpected error occurred.")   // NEVER leak ex.Message for unknown errors
        };

        if (status >= 500)
        {
            log.LogError(exception, "Unhandled exception");
        }
        else
        {
            log.LogInformation("Handled {Code}: {Message}", code, exception.Message);
        }

        httpContext.Response.StatusCode = status;
        var pd = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://lankamart.dev/errors/{code.ToLowerInvariant().Replace('_', '-')}"
        };
        pd.Extensions["code"] = code;
        pd.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = pd, Exception = exception });
    }
}
