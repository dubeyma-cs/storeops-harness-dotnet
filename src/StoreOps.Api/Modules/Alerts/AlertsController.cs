using Microsoft.AspNetCore.Mvc;

namespace StoreOps.Api.Modules.Alerts;

/// <summary>HTTP surface for the alerts module.</summary>
[ApiController]
[Route("api/alerts")]
[Produces("application/json")]
public sealed class AlertsController : ControllerBase
{
    private readonly IAlertService _alerts;

    public AlertsController(IAlertService alerts)
    {
        _alerts = alerts;
    }

    /// <summary>Gets alerts for the authenticated staff member, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AlertResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AlertResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        var alerts = await _alerts.ListForCurrentStaffAsync(cancellationToken);
        return Ok(alerts.Select(AlertResponse.From).ToList());
    }
}

/// <summary>Wire representation of an alert.</summary>
public sealed record AlertResponse(
    string Id,
    string StoreId,
    string RecipientStaffId,
    string Type,
    string Channel,
    string Status,
    string Title,
    string Body,
    string? RelatedActivityId,
    string? RelatedProgrammeId,
    DateTimeOffset CreatedAt)
{
    /// <summary>Projects the domain entity onto the wire contract.</summary>
    public static AlertResponse From(Notification notification) => new(
        notification.Id,
        notification.StoreId,
        notification.RecipientStaffId,
        notification.Type.ToString(),
        notification.Channel.ToString(),
        notification.Status.ToString(),
        notification.Title,
        notification.Body,
        notification.RelatedActivityId,
        notification.RelatedProgrammeId,
        notification.CreatedAt);
}
