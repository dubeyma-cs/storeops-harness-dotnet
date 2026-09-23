using System.Text.Json;

namespace StoreOps.Api.Shared.Errors;

/// <summary>
/// The single translation point between the <see cref="AppError"/> hierarchy and HTTP.
/// </summary>
/// <remarks>
/// Controllers never build error responses themselves — they let the error propagate. That is
/// what keeps the "no HTTP concerns in services, no business logic in routes" boundary honest:
/// a service says <c>throw new NotFoundError("Activity", id)</c> and this middleware is the only
/// code that knows a <c>NotFoundError</c> is a 404.
/// </remarks>
public sealed class AppErrorHandlingMiddleware
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<AppErrorHandlingMiddleware> _logger;

    public AppErrorHandlingMiddleware(RequestDelegate next, ILogger<AppErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppError error)
        {
            _logger.LogWarning(
                "Handled {ErrorCode} on {Method} {Path}: {Message}",
                error.Code,
                context.Request.Method,
                context.Request.Path,
                error.Message);

            await WriteAsync(context, error.StatusCode, error.Code, error.Message, error.Details);
        }
        catch (Exception unexpected)
        {
            // Anything reaching here is a defect: a code path escaped the AppError contract.
            _logger.LogError(
                unexpected,
                "Unhandled exception on {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            await WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ErrorCodes.Unexpected,
                "An unexpected error occurred.",
                details: null);
        }
    }

    private static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? details)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var payload = new ErrorResponse(
            new ErrorBody(code, message, statusCode, context.TraceIdentifier, details));

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }
}

/// <summary>Wire format for every StoreOps error response.</summary>
public sealed record ErrorResponse(ErrorBody Error);

/// <summary>Body of an error response.</summary>
public sealed record ErrorBody(
    string Code,
    string Message,
    int StatusCode,
    string TraceId,
    IReadOnlyDictionary<string, string[]>? Details);
