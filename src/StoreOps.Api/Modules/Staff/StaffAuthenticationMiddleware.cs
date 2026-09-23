using StoreOps.Api.Shared.Auth;
using StoreOps.Api.Shared.Errors;

namespace StoreOps.Api.Modules.Staff;

/// <summary>
/// Resolves the bearer token on each request into a <see cref="StaffContext"/>.
/// </summary>
/// <remarks>
/// This middleware lives in the staff module rather than in Shared because authentication is
/// the staff module's stated responsibility. Shared must stay dependency-free in the module
/// direction: Shared never imports a module, modules always import Shared.
/// <para>
/// The reference build authenticates against the fixed seed roster
/// (<c>Authorization: Bearer dev-token-store-manager</c>). Swapping this for Entra ID changes
/// only this file.
/// </para>
/// </remarks>
public sealed class StaffAuthenticationMiddleware
{
    private const string AnonymousPathPrefix = "/health";

    private readonly RequestDelegate _next;

    public StaffAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IStaffService staffService,
        IStaffContextAccessor accessor)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments(AnonymousPathPrefix))
        {
            await _next(context);
            return;
        }

        var token = ReadBearerToken(context.Request);
        var staff = token is null
            ? null
            : await staffService.AuthenticateAsync(token, context.RequestAborted);

        if (staff is null)
        {
            throw new UnauthorizedError(
                "A valid 'Authorization: Bearer <token>' header identifying store staff is required.");
        }

        accessor.Set(new StaffContext(
            StaffId: staff.Id,
            StoreId: staff.StoreId,
            RegionId: staff.RegionId,
            Role: staff.Role.ToString()));

        await _next(context);
    }

    private static string? ReadBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        const string scheme = "Bearer ";
        return header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? header[scheme.Length..].Trim()
            : null;
    }
}
