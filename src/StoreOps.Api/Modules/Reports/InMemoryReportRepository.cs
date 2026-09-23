using System.Collections.Concurrent;

namespace StoreOps.Api.Modules.Reports;

/// <summary>In-memory report store, registered as a singleton.</summary>
public sealed class InMemoryReportRepository : IReportRepository
{
    private readonly ConcurrentDictionary<string, Report> _reports = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<Report>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Report> result = _reports.Values
            .OrderByDescending(r => r.RequestedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<Report?> FindAsync(string reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_reports.TryGetValue(reportId, out var report) ? report : null);

    public Task AddAsync(Report report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        _reports[report.Id] = report;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Report report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        _reports[report.Id] = report;
        return Task.CompletedTask;
    }
}
