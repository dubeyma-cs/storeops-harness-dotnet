using Microsoft.Extensions.DependencyInjection;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Alerts;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Tests.TestSupport;

namespace StoreOps.Api.Tests.Alerts;

/// <summary>
/// End-to-end tests of the event-bus path: activities publishes, alerts reacts.
/// </summary>
/// <remarks>
/// These are the tests that would fail if someone "simplified" the design by injecting
/// <c>IAlertService</c> into <c>ActivityService</c> — the assertions are about an alert appearing
/// on the right person's list after an event, never about a direct call.
/// <para>
/// Each test builds its own host rather than sharing a class fixture. The SLA and escalation
/// sweeps are store-wide, so a breach left pending by one test would be swept by the next and
/// the counts asserted here would depend on test execution order.
/// </para>
/// </remarks>
public sealed class SlaAlertFlowTests
{
    private static readonly string LeadDepartment = InMemoryStaffRepository.SeedData.DepartmentLead.Department;

    [Fact]
    public async Task Given_an_overdue_CRITICAL_activity_When_the_SLA_sweep_runs_Then_the_department_lead_is_alerted()
    {
        using var factory = new StoreOpsApiFactory();
        var manager = factory.AsStoreManager();
        var lead = factory.AsDepartmentLead();

        var activity = await manager.CreateActivityAsync(
            title: "SLA: chilled compliance check",
            department: LeadDepartment,
            priority: ActivityPriority.CRITICAL,
            category: ActivityCategory.COMPLIANCE,
            dueAt: factory.Clock.UtcNow.AddHours(2));

        factory.Clock.Advance(TimeSpan.FromHours(3));

        var flagged = await factory.RunAsync(services =>
            services.GetRequiredService<IActivityService>().SweepSlaBreachesAsync());

        Assert.Equal(1, flagged);

        var alerts = await (await lead.GetAsync("/api/alerts")).ReadAsync<List<AlertResponse>>();

        var breach = Assert.Single(alerts);

        Assert.Equal(nameof(AlertType.SLA_BREACH), breach.Type);
        Assert.Equal(activity.Id, breach.RelatedActivityId);
        Assert.Equal(InMemoryStaffRepository.SeedData.DepartmentLead.Id, breach.RecipientStaffId);
        Assert.Contains("CRITICAL", breach.Body);
    }

    [Fact]
    public async Task Given_a_breach_already_raised_When_the_sweep_runs_again_Then_no_duplicate_alert_appears()
    {
        using var factory = new StoreOpsApiFactory();
        var manager = factory.AsStoreManager();
        var lead = factory.AsDepartmentLead();

        var activity = await manager.CreateActivityAsync(
            title: "SLA: idempotency check",
            department: LeadDepartment,
            priority: ActivityPriority.HIGH,
            dueAt: factory.Clock.UtcNow.AddMinutes(30));

        factory.Clock.Advance(TimeSpan.FromHours(1));

        var first = await factory.RunAsync(s => s.GetRequiredService<IActivityService>().SweepSlaBreachesAsync());
        var second = await factory.RunAsync(s => s.GetRequiredService<IActivityService>().SweepSlaBreachesAsync());

        Assert.Equal(1, first);
        Assert.Equal(0, second);

        var alerts = await (await lead.GetAsync("/api/alerts")).ReadAsync<List<AlertResponse>>();

        var breach = Assert.Single(alerts);
        Assert.Equal(activity.Id, breach.RelatedActivityId);
    }

    [Fact]
    public async Task Given_a_MEDIUM_activity_past_its_due_date_When_the_sweep_runs_Then_no_SLA_alert_is_raised()
    {
        using var factory = new StoreOpsApiFactory();
        var manager = factory.AsStoreManager();
        var lead = factory.AsDepartmentLead();

        await manager.CreateActivityAsync(
            title: "SLA: not tracked at MEDIUM",
            department: LeadDepartment,
            priority: ActivityPriority.MEDIUM,
            dueAt: factory.Clock.UtcNow.AddMinutes(10));

        factory.Clock.Advance(TimeSpan.FromHours(6));

        var flagged = await factory.RunAsync(s =>
            s.GetRequiredService<IActivityService>().SweepSlaBreachesAsync());

        Assert.Equal(0, flagged);

        var alerts = await (await lead.GetAsync("/api/alerts")).ReadAsync<List<AlertResponse>>();
        Assert.Empty(alerts);
    }

    [Fact]
    public async Task Given_an_unresolved_breach_When_the_grace_period_lapses_Then_it_escalates_to_store_management()
    {
        using var factory = new StoreOpsApiFactory();
        var manager = factory.AsStoreManager();

        var activity = await manager.CreateActivityAsync(
            title: "SLA: escalation path",
            department: LeadDepartment,
            priority: ActivityPriority.CRITICAL,
            dueAt: factory.Clock.UtcNow.AddMinutes(15));

        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        await factory.RunAsync(s => s.GetRequiredService<IActivityService>().SweepSlaBreachesAsync());

        // Still inside the four-hour grace period configured for tests.
        var early = await factory.RunAsync(s => s.GetRequiredService<IAlertService>().SweepEscalationsAsync());
        Assert.Equal(0, early);

        factory.Clock.Advance(TimeSpan.FromHours(5));
        var escalated = await factory.RunAsync(s => s.GetRequiredService<IAlertService>().SweepEscalationsAsync());

        Assert.Equal(1, escalated);

        var managerAlerts = await (await manager.GetAsync("/api/alerts")).ReadAsync<List<AlertResponse>>();

        var escalation = Assert.Single(managerAlerts, a => a.Type == nameof(AlertType.ESCALATION));
        Assert.Equal(activity.Id, escalation.RelatedActivityId);
        Assert.Equal(InMemoryStaffRepository.SeedData.StoreManager.Id, escalation.RecipientStaffId);
    }

    [Fact]
    public async Task Given_a_breach_resolved_before_the_grace_period_When_sweeping_Then_nothing_is_escalated()
    {
        using var factory = new StoreOpsApiFactory();
        var manager = factory.AsStoreManager();

        var activity = await manager.CreateActivityAsync(
            title: "SLA: resolved before escalation",
            department: LeadDepartment,
            priority: ActivityPriority.HIGH,
            dueAt: factory.Clock.UtcNow.AddMinutes(15));

        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        await factory.RunAsync(s => s.GetRequiredService<IActivityService>().SweepSlaBreachesAsync());

        // Completing the activity publishes ActivityStatusChangedEvent, which clears the escalation.
        await manager.SetStatusAsync(activity.Id, ActivityStatus.DONE);

        factory.Clock.Advance(TimeSpan.FromHours(8));
        var escalated = await factory.RunAsync(s => s.GetRequiredService<IAlertService>().SweepEscalationsAsync());

        Assert.Equal(0, escalated);

        var managerAlerts = await (await manager.GetAsync("/api/alerts")).ReadAsync<List<AlertResponse>>();
        Assert.DoesNotContain(managerAlerts, a => a.Type == nameof(AlertType.ESCALATION));
    }

    [Fact]
    public async Task Given_a_shift_handover_When_items_are_applied_Then_store_management_receives_a_handover_alert()
    {
        using var factory = new StoreOpsApiFactory();
        var lead = factory.AsDepartmentLead();
        var manager = factory.AsStoreManager();

        var activity = await lead.CreateActivityAsync(title: "Handover alert fixture");

        await lead.PatchJsonAsync(
            "/api/activities/bulk-status",
            new BulkStatusUpdateRequest
            {
                Items = { new BulkStatusItem { ActivityId = activity.Id, Status = ActivityStatus.DONE } },
            });

        var alerts = await (await manager.GetAsync("/api/alerts")).ReadAsync<List<AlertResponse>>();

        var handover = Assert.Single(alerts, a => a.Type == nameof(AlertType.SHIFT_HANDOVER));
        Assert.Contains(InMemoryStaffRepository.SeedData.DepartmentLead.Id, handover.Body);
        Assert.Contains("1 of 1", handover.Body);
    }

    [Fact]
    public async Task Given_an_alert_for_another_staff_member_When_listing_Then_it_is_not_visible_to_the_caller()
    {
        using var factory = new StoreOpsApiFactory();
        var manager = factory.AsStoreManager();
        var associate = factory.AsAssociate();

        await manager.CreateActivityAsync(
            title: "SLA: recipient scoping",
            department: LeadDepartment,
            priority: ActivityPriority.CRITICAL,
            dueAt: factory.Clock.UtcNow.AddMinutes(5));

        factory.Clock.Advance(TimeSpan.FromMinutes(10));
        await factory.RunAsync(s => s.GetRequiredService<IActivityService>().SweepSlaBreachesAsync());

        // The breach alert went to the department lead, so the associate must see nothing.
        var associateAlerts = await (await associate.GetAsync("/api/alerts")).ReadAsync<List<AlertResponse>>();
        Assert.Empty(associateAlerts);

        var leadAlerts = await (await factory.AsDepartmentLead().GetAsync("/api/alerts"))
            .ReadAsync<List<AlertResponse>>();
        Assert.Single(leadAlerts);
    }
}
