using System.Net;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Alerts;
using StoreOps.Api.Tests.TestSupport;

namespace StoreOps.Api.Tests.Alerts;

/// <summary>
/// Sprint 2 acceptance tests for the handover notification raised through the event bus.
/// </summary>
/// <remarks>
/// AC-13 is verified by
/// <c>SlaAlertFlowTests.Given_a_shift_handover_When_items_are_applied_Then_store_management_receives_a_handover_alert</c>;
/// AC-15 by <c>ArchitectureRuleTests.Given_the_StoreOps_source_tree_When_it_is_scanned_Then_the_module_graph_is_the_intended_one</c>.
/// This file covers AC-14 — the negative case, which is the one a "notify on handover" feature
/// most easily gets wrong.
/// <para>
/// Builds its own host: the assertion is about the store-wide alert list being empty, so a
/// leftover alert from a sibling test would break it.
/// </para>
/// </remarks>
public sealed class HandoverNotificationTests
{
    // AC-14
    [Fact]
    public async Task Given_a_handover_where_every_item_fails_When_it_is_applied_Then_no_handover_alert_is_raised()
    {
        using var factory = new StoreOpsApiFactory();
        var lead = factory.AsDepartmentLead();
        var manager = factory.AsStoreManager();

        var completed = await lead.CreateActivityAsync(title: "Handover: nothing to report");
        await lead.SetStatusAsync(completed.Id, ActivityStatus.DONE);

        var response = await lead.PatchJsonAsync(
            "/api/activities/bulk-status",
            new BulkStatusUpdateRequest
            {
                Items =
                {
                    new BulkStatusItem { ActivityId = completed.Id, Status = ActivityStatus.DONE },
                    new BulkStatusItem { ActivityId = "act-not-real", Status = ActivityStatus.DONE },
                },
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.ReadAsync<BulkStatusUpdateResponse>();
        Assert.Equal(0, body.Updated);

        // The event is still published — it is a fact. Raising an alert about it is the alerts
        // module's judgement, and "nothing changed" is not news for a store manager.
        var alerts = await (await manager.GetAsync("/api/alerts")).ReadAsync<List<AlertResponse>>();
        Assert.DoesNotContain(alerts, a => a.Type == nameof(AlertType.SHIFT_HANDOVER));
    }
}
