using Microsoft.AspNetCore.Mvc;

namespace StoreOps.Api.Modules.Activities;

/// <summary>
/// HTTP surface for the activities module.
/// </summary>
/// <remarks>
/// Routes do three things and nothing else: bind and shape-validate the request, call the
/// service, map the result to a status code. No business rule is decided here — notice that the
/// bulk-status handler chooses 200/207/409 purely from counts the service returned, and never
/// inspects an activity. Errors are not caught: <c>AppErrorHandlingMiddleware</c> owns that.
/// </remarks>
[ApiController]
[Route("api/activities")]
[Produces("application/json")]
public sealed class ActivitiesController : ControllerBase
{
    private readonly IActivityService _activities;

    public ActivitiesController(IActivityService activities)
    {
        _activities = activities;
    }

    /// <summary>Lists activities for the authenticated store, optionally filtered.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ActivityResponse>>> ListAsync(
        [FromQuery] string? programmeId,
        [FromQuery] ActivityStatus? status,
        CancellationToken cancellationToken)
    {
        var activities = await _activities.ListAsync(programmeId, status, cancellationToken);
        return Ok(activities.Select(ActivityResponse.From).ToList());
    }

    /// <summary>Creates a new operational activity.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ActivityResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ActivityResponse>> CreateAsync(
        [FromBody] CreateActivityRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _activities.CreateAsync(request, cancellationToken);

        return CreatedAtAction(
            nameof(GetAsync),
            new { id = created.Id },
            ActivityResponse.From(created));
    }

    /// <summary>
    /// Applies a shift-handover bulk status change to many activities in one request.
    /// </summary>
    /// <remarks>
    /// <c>bulk-status</c> is a literal segment, and ASP.NET Core route precedence ranks literals
    /// above the <c>{id}</c> parameter template, so this action always wins over
    /// <see cref="UpdateAsync"/> for <c>PATCH /api/activities/bulk-status</c>.
    /// <para>
    /// Status selection is mechanical, which is what makes it testable:
    /// all items succeeded → 200; all items failed → 409; anything in between → 207 Multi-Status.
    /// </para>
    /// </remarks>
    [HttpPatch("bulk-status")]
    [ProducesResponseType(typeof(BulkStatusUpdateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BulkStatusUpdateResponse), StatusCodes.Status207MultiStatus)]
    [ProducesResponseType(typeof(BulkStatusUpdateResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkStatusUpdateResponse>> BulkUpdateStatusAsync(
        [FromBody] BulkStatusUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _activities.BulkUpdateStatusAsync(request, cancellationToken);

        var statusCode = (result.Updated, result.Failed) switch
        {
            (_, 0) => StatusCodes.Status200OK,
            (0, _) => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status207MultiStatus,
        };

        return StatusCode(statusCode, result);
    }

    /// <summary>Gets a single activity by id.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ActivityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActivityResponse>> GetAsync(string id, CancellationToken cancellationToken)
    {
        var activity = await _activities.GetAsync(id, cancellationToken);
        return Ok(ActivityResponse.From(activity));
    }

    /// <summary>Updates status, priority, category or assignee of an activity.</summary>
    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(ActivityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ActivityResponse>> UpdateAsync(
        string id,
        [FromBody] UpdateActivityRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _activities.UpdateAsync(id, request, cancellationToken);
        return Ok(ActivityResponse.From(updated));
    }

    /// <summary>Deletes an activity. Permitted for the creator or a store manager only.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _activities.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Returns the append-only audit trail for an activity.</summary>
    [HttpGet("{id}/audit")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityAuditEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ActivityAuditEntry>>> ListAuditAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var entries = await _activities.ListAuditAsync(id, cancellationToken);
        return Ok(entries);
    }
}
