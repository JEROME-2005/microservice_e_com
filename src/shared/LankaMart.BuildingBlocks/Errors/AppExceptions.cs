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
