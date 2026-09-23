using Microsoft.AspNetCore.Mvc;

namespace StoreOps.Api.Modules.Programmes;

/// <summary>HTTP surface for the programmes module.</summary>
[ApiController]
[Route("api/programmes")]
[Produces("application/json")]
public sealed class ProgrammesController : ControllerBase
{
    private readonly IProgrammeService _programmes;

    public ProgrammesController(IProgrammeService programmes)
    {
        _programmes = programmes;
    }

    /// <summary>Lists programmes for the authenticated store.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProgrammeResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProgrammeResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        var programmes = await _programmes.ListAsync(cancellationToken);
        return Ok(programmes.Select(ProgrammeResponse.From).ToList());
    }

    /// <summary>Creates a new store programme.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProgrammeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProgrammeResponse>> CreateAsync(
        [FromBody] CreateProgrammeRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _programmes.CreateAsync(request, cancellationToken);

        return CreatedAtAction(
            nameof(GetAsync),
            new { id = created.Id },
            ProgrammeResponse.From(created));
    }

    /// <summary>Gets a single programme by id.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ProgrammeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProgrammeResponse>> GetAsync(string id, CancellationToken cancellationToken)
    {
        var programme = await _programmes.GetAsync(id, cancellationToken);
        return Ok(ProgrammeResponse.From(programme));
    }

    /// <summary>Adds a staff member to a programme.</summary>
    [HttpPost("{id}/members")]
    [ProducesResponseType(typeof(ProgrammeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProgrammeResponse>> AddMemberAsync(
        string id,
        [FromBody] AddProgrammeMemberRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _programmes.AddMemberAsync(id, request, cancellationToken);

        return CreatedAtAction(
            nameof(GetAsync),
            new { id = updated.Id },
            ProgrammeResponse.From(updated));
    }

    /// <summary>Closes a programme, which triggers a store summary report via the event bus.</summary>
    [HttpPost("{id}/close")]
    [ProducesResponseType(typeof(ProgrammeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProgrammeResponse>> CloseAsync(string id, CancellationToken cancellationToken)
    {
        var closed = await _programmes.CloseAsync(id, cancellationToken);
        return Ok(ProgrammeResponse.From(closed));
    }
}
