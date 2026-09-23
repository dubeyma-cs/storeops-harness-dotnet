using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Reports;

/// <summary>Business layer of the reports module.</summary>
public interface IReportService
{
    /// <summary>Builds a store summary on demand from the current state of the other modules.</summary>
    Task<Report> GenerateStoreSummaryAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>Lists reports generated so far.</summary>
    Task<IReadOnlyList<Report>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reacts to a closed programme by queueing and generating a STORE_SUMMARY report.</summary>
    Task HandleProgrammeClosedAsync(ProgrammeClosedEvent closed, CancellationToken cancellationToken = default);
}
