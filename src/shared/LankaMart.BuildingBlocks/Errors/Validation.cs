using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LankaMart.BuildingBlocks;

/// <summary>Runs the registered FluentValidation validator for the request body of type T before the handler.</summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var arg = context.Arguments.OfType<T>().FirstOrDefault();
        if (arg is null)
        {
            return Results.Problem(statusCode: 400, title: "Request body is required",
                extensions: new Dictionary<string, object?> { ["code"] = "BODY_REQUIRED" });
        }

        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is not null)
        {
            var result = await validator.ValidateAsync(arg, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary(),
                    extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
            }
        }
        return await next(context);
    }
}

public static class ValidationExtensions
{
    /// <summary>Validate the request body of type T before the handler runs.</summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder b) where T : class
        => b.AddEndpointFilter<ValidationFilter<T>>();
}
