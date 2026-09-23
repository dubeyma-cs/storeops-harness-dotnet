using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Programmes;
using StoreOps.Api.Modules.Reports;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Tests.TestSupport;

namespace StoreOps.Api.Tests.Reports;

/// <summary>
/// Tests the reports module, which has no REST surface in the baseline: it is reachable only
/// through the event bus and through cross-module read-only lookups.
/// </summary>
/// <remarks>
/// Each test builds its own host: report aggregates are store-wide, so shared state between
/// tests would make the expected counts depend on execution order.
/// </remarks>
public sealed class ReportEventFlowTests
{
    [Fact]
    public async Task Given_a_closed_programme_When_the_event_is_published_Then_a_store_summary_report_is_generated()
    {
        using var factory = new StoreOpsApiFactory();
        var manager = factory.AsStoreManager();

        var programme = await (await manager.PostAsJsonAsync(
                "/api/programmes",
                new CreateProgrammeRequest { Name = "Rollout to be closed" },
                ApiClient.Json))
            .ReadAsync<ProgrammeResponse>();

        var done = await manager.CreateActivityAsync(title: "Report fixture: completed");
        await manager.CreateActivityAsync(title: "Report fixture: still open");
        await manager.SetStatusAsync(done.Id, ActivityStatus.DONE);

        await manager.PostAsync($"/api/programmes/{programme.Id}/close", content: null);

        var reports = await factory.RunAsync(s => s.GetRequiredService<IReportService>().ListAsync());

        var report = Assert.Single(reports);

        Assert.Equal(ReportType.STORE_SUMMARY, report.Type);
        Assert.Equal(ReportStatus.READY, report.Status);
        Assert.Equal(InMemoryStaffRepository.SeedData.StoreId, report.ScopeId);
        Assert.NotNull(report.TriggeredByEventId);

        var payload = Assert.IsType<ReportPayload>(report.Payload);
        Assert.Equal(2, payload.TotalActivities);
        Assert.Equal(1, payload.CompletedActivities);
        Assert.Equal(0.5d, payload.CompletionRate);
        Assert.Equal(1, payload.ProgrammeCount);
        Assert.Equal(InMemoryStaffRepository.SeedData.Staff.Count, payload.StaffCount);
    }

    [Fact]
    public async Task Given_overdue_and_blocked_activities_When_a_summary_is_generated_Then_the_breakdown_is_correct()
    {
        using var factory = new StoreOpsApiFactory();
        var manager = factory.AsStoreManager();

        await manager.CreateActivityAsync(
            title: "Report fixture: overdue audit",
            category: ActivityCategory.AUDIT,
            priority: ActivityPriority.HIGH,
            dueAt: factory.Clock.UtcNow.AddHours(1));

        await manager.CreateActivityAsync(
            title: "Report fixture: overdue restock",
            category: ActivityCategory.RESTOCKING,
            dueAt: factory.Clock.UtcNow.AddHours(2));

        var blocked = await manager.CreateActivityAsync(title: "Report fixture: blocked");
        await manager.SetStatusAsync(blocked.Id, ActivityStatus.BLOCKED, "Waiting on a replacement chiller");

        factory.Clock.Advance(TimeSpan.FromHours(4));

        var report = await factory.RunAsync(s =>
            s.GetRequiredService<IReportService>()
                .GenerateStoreSummaryAsync(InMemoryStaffRepository.SeedData.StoreId));

        var payload = Assert.IsType<ReportPayload>(report.Payload);

        Assert.Equal(3, payload.TotalActivities);
        Assert.Equal(0, payload.CompletedActivities);
        Assert.Equal(0d, payload.CompletionRate);
        Assert.Equal(2, payload.OverdueActivities);
        Assert.Equal(1, payload.OverdueByCategory[nameof(ActivityCategory.AUDIT)]);
        Assert.Equal(1, payload.OverdueByCategory[nameof(ActivityCategory.RESTOCKING)]);
        Assert.False(payload.OverdueByCategory.ContainsKey(nameof(ActivityCategory.PLANOGRAM)));

        var blockedId = Assert.Single(payload.BlockedActivityIds);
        Assert.Equal(blocked.Id, blockedId);

        // The blocked activity has no due date, so it must not be counted as overdue.
        Assert.Equal(
            2,
            payload.OverdueByCategory.Values.Sum());
    }

    [Fact]
    public async Task Given_a_store_with_no_activities_When_a_summary_is_generated_Then_the_completion_rate_is_zero()
    {
        using var factory = new StoreOpsApiFactory();

        var report = await factory.RunAsync(s =>
            s.GetRequiredService<IReportService>()
                .GenerateStoreSummaryAsync(InMemoryStaffRepository.SeedData.StoreId));

        var payload = Assert.IsType<ReportPayload>(report.Payload);

        // Guards the divide-by-zero path rather than asserting a status code.
        Assert.Equal(0, payload.TotalActivities);
        Assert.Equal(0d, payload.CompletionRate);
        Assert.Empty(payload.OverdueByCategory);
        Assert.Empty(payload.BlockedActivityIds);
    }
}
