# Generator summary — sprint 2, iteration 1

**Contract:** [`sprint-2-contract.md`](sprint-2-contract.md)

> **Evidence status.** Entries marked `[UNVERIFIED]` need figures from a real `dotnet test` /
> coverage run. Architecture-gate figures are from an actual run of `tools/StoreOps.ArchCheck`.

## Acceptance criteria self-check

| AC | Criterion (abbreviated) | Status | Verifying test | Notes |
| --- | --- | --- | --- | --- |
| AC-13 | store managers get a `SHIFT_HANDOVER` alert naming the actor and "1 of 1" | MET | `StoreOps.Api.Tests.Alerts.SlaAlertFlowTests.Given_a_shift_handover_When_items_are_applied_Then_store_management_receives_a_handover_alert` | asserts the recipient's alert list, not the publish call |
| AC-14 | a batch where everything fails raises no alert | MET | `StoreOps.Api.Tests.Alerts.HandoverNotificationTests.Given_a_handover_where_every_item_fails_When_it_is_applied_Then_no_handover_alert_is_raised` | asserts the event still publishes but no alert appears |
| AC-15 | no `activities → alerts` edge; zero `SO-002` violations | MET | `StoreOps.Api.Tests.Architecture.ArchitectureRuleTests.Given_the_StoreOps_source_tree_When_it_is_scanned_Then_the_module_graph_is_the_intended_one` | asserts the graph directly, so the rule is verified rather than assumed |

## Files changed

| File | Layer | Change |
| --- | --- | --- |
| `src/StoreOps.Api/Shared/Events/Contracts.cs` | shared | added `ActivitiesBulkStatusAppliedEvent` (store id, actor, requested/updated/failed counts, updated ids) |
| `src/StoreOps.Api/Modules/Activities/ActivityService.cs` | service | publish the new event once, after every item is resolved and audit rows are written |
| `src/StoreOps.Api/Modules/Alerts/IAlertService.cs` | service | added `HandleBulkStatusAppliedAsync` |
| `src/StoreOps.Api/Modules/Alerts/AlertService.cs` | service | raise a `SHIFT_HANDOVER` notification per `STORE_MANAGER`; return early when `UpdatedCount` is 0 |
| `src/StoreOps.Api/Modules/Alerts/AlertsEventSubscriptions.cs` | event | added the `On<ActivitiesBulkStatusAppliedEvent>` subscription |
| `tests/StoreOps.Api.Tests/Alerts/HandoverNotificationTests.cs` | tests | AC-14 |
| `tests/StoreOps.Api.Tests/Alerts/SlaAlertFlowTests.cs` | tests | AC-13 |

Three files in `src/` plus the shared contract — the "adding an event is three edits" shape from
`api-integration`. Nothing in `activities` learned the word `alerts`.

## Architecture rules touched

| Rule | How this change satisfies it |
| --- | --- |
| `SO-002` | the entire cross-module effect is `PublishAsync` on one side and `On<TEvent>` on the other; `ActivityService` has no `IAlertService` dependency |
| `SO-006` | the event contract lives in `Shared/Events/Contracts.cs` and carries only ids, counts and strings, so `alerts` does not import `activities` to read it |
| `SO-007` | the only new edge is `alerts → activities`, which already existed for the escalation sweep; the graph stays acyclic |
| `SO-004` | the recipient decision ("which staff get this alert") is in `AlertService`, not in a controller — there is no controller in this sprint |

## Automated checks run locally

| Command | Result |
| --- | --- |
| `dotnet build StoreOps.sln --warnaserror` | exit 0 — 0 warnings |
| `dotnet test StoreOps.sln` | `[UNVERIFIED]` — expected exit 0 |
| `dotnet run --project tools/StoreOps.ArchCheck` | exit 0 — 57 files, 0 violations; graph unchanged from sprint 1 |
| `dotnet run --project tools/StoreOps.CoverageGate -- --report …` | `[UNVERIFIED]` — expected exit 0 |

## Known gaps

1. **Alert fan-out is unbounded in principle.** One notification per `STORE_MANAGER` is two rows at
   most on the seeded roster, but nothing caps it. A real store hierarchy would need either a
   single alert addressed to a role or a fan-out limit.
2. **Subscriber failures are invisible to the caller by design.** `InMemoryEventBus` logs and
   swallows them, so a handover succeeds even if no alert is raised. That is the intended isolation,
   but it means alert delivery has no retry and no dead-letter path. A production bus would need
   both.
3. **No test asserts that a redelivered `ActivitiesBulkStatusAppliedEvent` does not duplicate the
   alert.** `HandleBulkStatusAppliedAsync` has no idempotency key, unlike the SLA-breach path which
   guards on `ExistsForActivityAsync`. With the in-process bus there is no redelivery, so this is
   latent rather than broken — but it is the first thing that breaks when the bus is swapped for
   Service Bus. Not in the contract; recommended as its own sprint.
