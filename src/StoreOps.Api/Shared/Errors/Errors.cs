namespace StoreOps.Api.Shared.Errors;

/// <summary>400 — request shape or field values are not acceptable.</summary>
public sealed class ValidationError : AppError
{
    public ValidationError(string message)
        : base(ErrorCodes.Validation, message, StatusCodes.Status400BadRequest)
    {
    }

    public ValidationError(string message, IReadOnlyDictionary<string, string[]> details)
        : base(ErrorCodes.Validation, message, StatusCodes.Status400BadRequest)
    {
        Details = details;
    }

    /// <summary>Builds a validation error for a single named field.</summary>
    public static ValidationError ForField(string field, string problem) =>
        new(
            $"Validation failed for '{field}'.",
            new Dictionary<string, string[]> { [field] = new[] { problem } });
}

/// <summary>404 — the addressed resource does not exist for the caller's store scope.</summary>
public sealed class NotFoundError : AppError
{
    public NotFoundError(string resource, string id)
        : base(ErrorCodes.NotFound, $"{resource} '{id}' was not found.", StatusCodes.Status404NotFound)
    {
        Resource = resource;
        ResourceId = id;
    }

    public string Resource { get; }

    public string ResourceId { get; }
}

/// <summary>409 — the request is well formed but conflicts with current state.</summary>
public sealed class ConflictError : AppError
{
    public ConflictError(string message)
        : base(ErrorCodes.Conflict, message, StatusCodes.Status409Conflict)
    {
    }
}

/// <summary>409 — an activity status change is not permitted by the lifecycle rules.</summary>
public sealed class InvalidStateTransitionError : AppError
{
    public InvalidStateTransitionError(string from, string to)
        : base(
            ErrorCodes.InvalidStateTransition,
            $"Transition from '{from}' to '{to}' is not permitted.",
            StatusCodes.Status409Conflict)
    {
        From = from;
        To = to;
    }

    public string From { get; }

    public string To { get; }
}

/// <summary>403 — the caller is authenticated but lacks the required staff role or ownership.</summary>
public sealed class ForbiddenError : AppError
{
    public ForbiddenError(string message)
        : base(ErrorCodes.Forbidden, message, StatusCodes.Status403Forbidden)
    {
    }
}

/// <summary>401 — no usable staff identity on the request.</summary>
public sealed class UnauthorizedError : AppError
{
    public UnauthorizedError(string message)
        : base(ErrorCodes.Unauthorized, message, StatusCodes.Status401Unauthorized)
    {
    }
}
