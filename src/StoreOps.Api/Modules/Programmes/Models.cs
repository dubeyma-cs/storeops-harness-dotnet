namespace StoreOps.Api.Modules.Programmes;

/*
 * Naming note (see .harness/skills/app-context/SKILL.md):
 * the specification's `Project`, `ProjectMember` and `ProjectRole` are modelled as
 * `Programme`, `ProgrammeMember` and `ProgrammeRole` to match the module name and the
 * /api/programmes route. Role values are kept verbatim.
 */

/// <summary>A staff member's role within a store programme.</summary>
public enum ProgrammeRole
{
    ASSOCIATE = 0,
    DEPARTMENT_LEAD = 1,
    STORE_MANAGER = 2,
}

/// <summary>Lifecycle state of a store programme.</summary>
public enum ProgrammeStatus
{
    PLANNED = 0,
    ACTIVE = 1,
    CLOSED = 2,
}

/// <summary>A store programme — a seasonal rollout, compliance drive or refit.</summary>
public sealed record Programme
{
    public required string Id { get; init; }

    public required string StoreId { get; init; }

    public required string RegionId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public ProgrammeStatus Status { get; init; } = ProgrammeStatus.PLANNED;

    public required string OwnerStaffId { get; init; }

    public DateTimeOffset? StartsOn { get; init; }

    public DateTimeOffset? EndsOn { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public IReadOnlyList<ProgrammeMember> Members { get; init; } = Array.Empty<ProgrammeMember>();
}

/// <summary>Membership of a staff member in a programme.</summary>
public sealed record ProgrammeMember(
    string StaffId,
    ProgrammeRole Role,
    DateTimeOffset AddedAt,
    string AddedByStaffId);

/// <summary>
/// Read-only projection of a programme, exposed to other modules through
/// <see cref="IProgrammeService"/>.
/// </summary>
public sealed record ProgrammeReadModel(
    string Id,
    string StoreId,
    string RegionId,
    string Name,
    string Status,
    int MemberCount,
    DateTimeOffset? ClosedAt);
