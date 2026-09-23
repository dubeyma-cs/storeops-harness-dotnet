using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Programmes;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Events;
using StoreOps.Api.Shared.Time;

namespace StoreOps.Api.Modules.Reports;

/// <summary>
/// Aggregates activity, programme and staff data into report records.
/// </summary>
/// <remarks>
/// Every cross-module call in this file is a <c>List…</c> or <c>Find…</c> on another module's
/// service. There is no path from here that mutates an activity, a programme or a staff member —
/// that is the read-only reports rule, and <c>StoreOps.ArchCheck</c> rejects any call from this
/// module to a service method whose name is not a read verb.
/// </remarks>
public sealed class ReportService : IReportService
{
    private const string StatusDone = "DONE";
    private const string StatusBlocked = "BLOCKED";

    private readonly IReportRepository _repository;
    private readonly IActivityService _activityService;
    private readonly IProgrammeService _programmeService;
    private readonly IStaffService _staffService;
    private readonly IClock _clock;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IReportRepository repository,
        IActivityService activityService,
        IProgrammeService programmeService,
        IStaffService staffService,
        IClock clock,
        ILogger<ReportService> logger)
    {
        _repository = repository;
        _activityService = activityService;
        _programmeService = programmeService;
        _staffService = staffService;
        _clock = clock;
        _logger = logger;
    }

    public Task<IReadOnlyList<Report>> ListAsync(CancellationToken cancellationToken = default) =>
        _repository.ListAsync(cancellationToken);

    public async Task<Report> GenerateStoreSummaryAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        var report = await QueueAsync(ReportType.STORE_SUMMARY, storeId, triggeredByEventId: null, cancellationToken);
        return await FulfilAsync(report, storeId, cancellationToken);
    }

    public async Task HandleProgrammeClosedAsync(
        ProgrammeClosedEvent closed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(closed);

        var report = await QueueAsync(
            ReportType.STORE_SUMMARY, closed.StoreId, closed.EventId, cancellationToken);

        await FulfilAsync(report, closed.StoreId, cancellationToken);

        _logger.LogInformation(
            "Queued {ReportType} {ReportId} for store {StoreId} after programme {ProgrammeId} closed",
            report.Type,
            report.Id,
            closed.StoreId,
            closed.ProgrammeId);
    }

    private async Task<Report> QueueAsync(
        ReportType type,
        string scopeId,
        string? triggeredByEventId,
        CancellationToken cancellationToken)
    {
        var report = new Report
        {
            Id = $"rpt-{Guid.NewGuid():N}"[..16],
            Type = type,
            Status = ReportStatus.PENDING,
            ScopeId = scopeId,
            TriggeredByEventId = triggeredByEventId,
            RequestedAt = _clock.UtcNow,
        };

        await _repository.AddAsync(report, cancellationToken);
        return report;
    }

    private async Task<Report> FulfilAsync(Report report, string storeId, CancellationToken cancellationToken)
    {
        // Read-only cross-module lookups through service layers.
        var activities = await _activityService.ListForStoreAsync(storeId, cancellationToken);
        var programmes = await _programmeService.ListForStoreAsync(storeId, cancellationToken);
        var staff = await _staffService.ListForStoreAsync(storeId, cancellationToken);

        var total = activities.Count;
        var completed = activities.Count(a => string.Equals(a.Status, StatusDone, StringComparison.Ordinal));
        var blocked = activities
            .Where(a => string.Equals(a.Status, StatusBlocked, StringComparison.Ordinal))
            .Select(a => a.Id)
            .ToList();
        var overdue = activities.Where(a => a.IsOverdue).ToList();

        var payload = new ReportPayload(
            TotalActivities: total,
            CompletedActivities: completed,
            OverdueActivities: overdue.Count,
            BlockedActivities: blocked.Count,
            CompletionRate: total == 0 ? 0d : Math.Round((double)completed / total, 4),
            OverdueByCategory: overdue
                .GroupBy(a => a.Category, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            BlockedActivityIds: blocked,
            ProgrammeCount: programmes.Count,
            StaffCount: staff.Count);

        var ready = report with
        {
            Status = ReportStatus.READY,
            Payload = payload,
            CompletedAt = _clock.UtcNow,
        };

        await _repository.UpdateAsync(ready, cancellationToken);
        return ready;
    }
}
