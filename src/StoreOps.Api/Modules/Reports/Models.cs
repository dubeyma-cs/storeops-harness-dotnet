namespace StoreOps.Api.Modules.Reports;

/// <summary>What a report summarises.</summary>
public enum ReportType
{
    STORE_SUMMARY = 0,
    REGIONAL_ROLLUP = 1,
    DEPARTMENT_PERFORMANCE = 2,
}

/// <summary>Generation state of a report.</summary>
public enum ReportStatus
{
    PENDING = 0,
    READY = 1,
    FAILED = 2,
}

/// <summary>A generated performance summary.</summary>
public sealed record Report
{
    public required string Id { get; init; }

    public required ReportType Type { get; init; }

    public ReportStatus Status { get; init; } = ReportStatus.PENDING;

    public required string ScopeId { get; init; }

    public string? TriggeredByEventId { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public ReportPayload? Payload { get; init; }

    public string? FailureReason { get; init; }
}

/// <summary>Aggregated figures a report carries.</summary>
public sealed record ReportPayload(
    int TotalActivities,
    int CompletedActivities,
    int OverdueActivities,
    int BlockedActivities,
    double CompletionRate,
    IReadOnlyDictionary<string, int> OverdueByCategory,
    IReadOnlyList<string> BlockedActivityIds,
    int ProgrammeCount,
    int StaffCount);
