using System.ComponentModel.DataAnnotations;

namespace StoreOps.Api.Modules.Programmes;

/// <summary>Request body for <c>POST /api/programmes</c>.</summary>
public sealed class CreateProgrammeRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(140, MinimumLength = 3)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    public DateTimeOffset? StartsOn { get; set; }

    public DateTimeOffset? EndsOn { get; set; }
}

/// <summary>Request body for <c>POST /api/programmes/{id}/members</c>.</summary>
public sealed class AddProgrammeMemberRequest
{
    [Required(AllowEmptyStrings = false)]
    public string StaffId { get; set; } = string.Empty;

    [Required]
    public ProgrammeRole Role { get; set; } = ProgrammeRole.ASSOCIATE;
}

/// <summary>Wire representation of a programme member.</summary>
public sealed record ProgrammeMemberResponse(string StaffId, string Role, DateTimeOffset AddedAt);

/// <summary>Wire representation of a programme.</summary>
public sealed record ProgrammeResponse(
    string Id,
    string StoreId,
    string RegionId,
    string Name,
    string? Description,
    string Status,
    string OwnerStaffId,
    DateTimeOffset? StartsOn,
    DateTimeOffset? EndsOn,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<ProgrammeMemberResponse> Members)
{
    /// <summary>Projects the domain entity onto the wire contract.</summary>
    public static ProgrammeResponse From(Programme programme) => new(
        programme.Id,
        programme.StoreId,
        programme.RegionId,
        programme.Name,
        programme.Description,
        programme.Status.ToString(),
        programme.OwnerStaffId,
        programme.StartsOn,
        programme.EndsOn,
        programme.CreatedAt,
        programme.UpdatedAt,
        programme.ClosedAt,
        programme.Members
            .Select(m => new ProgrammeMemberResponse(m.StaffId, m.Role.ToString(), m.AddedAt))
            .ToList());
}
