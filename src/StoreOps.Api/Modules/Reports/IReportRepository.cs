namespace StoreOps.Api.Modules.Reports;

/// <summary>
/// Data access for the reports module — the <em>only</em> repository this module may write to.
/// </summary>
/// <remarks>
/// StoreOps architecture rule "read-only reports": reports aggregates data from activities,
/// programmes and staff but never writes to them. It does persist its own <see cref="Report"/>
/// records, which is why this interface exists at all. <c>StoreOps.ArchCheck</c> enforces the
/// rule by rejecting any call from a reports file to a non-read method on another module's
/// service.
/// </remarks>
public interface IReportRepository
{
    Task<IReadOnlyList<Report>> ListAsync(CancellationToken cancellationToken = default);

    Task<Report?> FindAsync(string reportId, CancellationToken cancellationToken = default);

    Task AddAsync(Report report, CancellationToken cancellationToken = default);

    Task UpdateAsync(Report report, CancellationToken cancellationToken = default);
}
