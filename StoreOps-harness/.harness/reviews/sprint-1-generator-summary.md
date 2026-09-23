# Generator summary — sprint 1, iteration 2

**Contract:** [`sprint-1-contract.md`](sprint-1-contract.md)
**Previous iteration:** FAIL (see [`sprint-1-evaluator-feedback.md`](sprint-1-evaluator-feedback.md),
iteration 1) — two ACs had no test, and the handover status set was duplicated in the controller.

> **Evidence status.** Entries marked `[UNVERIFIED]` are placeholders for figures that must come
> from a real `dotnet test` / coverage run on the assembled repository. Architecture-gate figures
> are from an actual run of `tools/StoreOps.ArchCheck`.

## Acceptance criteria self-check

| AC | Criterion (abbreviated) | Status | Verifying test | Notes |
| --- | --- | --- | --- | --- |
| AC-1 | all items valid → `200`, both applied | MET | `StoreOps.Api.Tests.Activities.BulkStatusHandoverTests.Given_open_activities_When_all_items_are_valid_Then_200_and_every_item_is_applied` | asserts `completedAt` set for `DONE` and null for `BLOCKED` |
| AC-2 | mixed → `207`, valid item still applied | MET | `…BulkStatusHandoverTests.Given_a_mix_of_valid_and_invalid_items_When_handing_over_Then_207_and_valid_items_still_apply` | asserts both per-item error codes and the surviving change |
| AC-3 | all items fail → `409`, nothing changed | MET | `…BulkStatusHandoverTests.Given_every_item_is_invalid_When_handing_over_Then_409_and_nothing_changes` | |
| AC-4 | one audit entry per updated activity | MET | `…BulkStatusHandoverTests.Given_an_updated_activity_When_the_handover_completes_Then_one_audit_entry_records_it` | added in iteration 2; asserts `source`, `action`, from/to, reason, actor, timestamp |
| AC-5 | no audit entry for a failed item | MET | `…BulkStatusHandoverTests.Given_a_failed_item_When_the_handover_completes_Then_no_audit_entry_is_written_for_it` | added in iteration 2 |
| AC-6 | status outside the handover set → item fails | MET | `…BulkStatusHandoverTests.Given_a_status_outside_the_handover_set_When_handing_over_Then_that_item_fails_validation` | |
| AC-7 | `BLOCKED` without a reason → item fails | MET | `…BulkStatusHandoverTests.Given_a_BLOCKED_item_without_a_reason_When_handing_over_Then_that_item_fails` | |
| AC-8 | duplicate ids → whole request `400` | MET | `…BulkStatusHandoverTests.Given_duplicate_activity_ids_When_handing_over_Then_the_whole_request_is_rejected` | asserts the activity is untouched |
| AC-9 | empty `items` → `400` | MET | `…BulkStatusHandoverTests.Given_an_empty_item_list_When_handing_over_Then_the_request_is_rejected` | `[MinLength(1)]` |
| AC-10 | 101 items → `400` | MET | `…BulkStatusHandoverTests.Given_more_items_than_the_cap_When_handing_over_Then_the_request_is_rejected` | `[MaxLength(100)]` plus a service re-check |
| AC-11 | no bearer token → `401` | MET | `…BulkStatusHandoverTests.Given_no_bearer_token_When_handing_over_Then_the_request_is_unauthenticated` | asserts `UNAUTHENTICATED` |
| AC-12 | literal route segment wins over `{id}` | MET | `…BulkStatusHandoverTests.Given_a_handover_request_When_items_are_applied_Then_the_route_is_not_bound_as_an_activity_id` | |

## Files changed

| File | Layer | Change |
| --- | --- | --- |
| `src/StoreOps.Api/Modules/Activities/Dtos.cs` | routes | added `BulkStatusItem`, `BulkStatusUpdateRequest` (with `MaxItems`), `BulkStatusItemResult`, `BulkStatusUpdateResponse` |
| `src/StoreOps.Api/Modules/Activities/ActivityStatusPolicy.cs` | service | added `HandoverStatuses`, `IsHandoverStatus`, `RequiresReason` — shared with the single-activity path |
| `src/StoreOps.Api/Modules/Activities/IActivityRepository.cs` | repository | added `FindManyAsync`, `AppendAuditAsync`, `ListAuditAsync` |
| `src/StoreOps.Api/Modules/Activities/InMemoryActivityRepository.cs` | repository | implemented the three additions; audit storage is append-only |
| `src/StoreOps.Api/Modules/Activities/IActivityService.cs` | service | added `BulkUpdateStatusAsync`, `ListAuditAsync` |
| `src/StoreOps.Api/Modules/Activities/ActivityService.cs` | service | added `BulkUpdateStatusAsync` and `ValidateHandoverItem`; **iteration 2:** no other change |
| `src/StoreOps.Api/Modules/Activities/ActivitiesController.cs` | routes | added the `bulk-status` and `{id}/audit` actions; **iteration 2:** removed the status filter the Evaluator flagged |
| `tests/StoreOps.Api.Tests/Activities/BulkStatusHandoverTests.cs` | tests | 12 acceptance tests; **iteration 2:** added the two audit tests (AC-4, AC-5) |
| `tests/StoreOps.Api.Tests/TestSupport/ActivityFixtures.cs` | tests | `CreateActivityAsync` / `SetStatusAsync` helpers that go through the real API |

Iteration 1 also reformatted `src/StoreOps.Api/Modules/Programmes/ProgrammeService.cs`; that change
was reverted in iteration 2 after the Evaluator's scope finding.

## Architecture rules touched

| Rule | How this change satisfies it |
| --- | --- |
| `SO-001` | the batch path uses only `IActivityRepository`; no other module's repository is referenced |
| `SO-002` | no cross-module call added. The event publication is sprint 2 |
| `SO-003` | whole-request failures throw `ValidationError`; per-item failures carry `ErrorCodes` values as data, so partial success remains possible |
| `SO-004` | after iteration 2, the controller reads only `result.Updated` and `result.Failed`; the handover status set lives in `ActivityStatusPolicy` |
| `SO-006` | no new type added to `Shared` |

## Automated checks run locally

| Command | Result |
| --- | --- |
| `dotnet build StoreOps.sln --warnaserror` | exit 0 — 0 warnings, 0 errors |
| `dotnet test StoreOps.sln` | `[UNVERIFIED]` — expected exit 0 |
| `dotnet run --project tools/StoreOps.ArchCheck` | exit 0 — 57 files scanned, 0 violations across `SO-001`…`SO-007` |
| `dotnet run --project tools/StoreOps.CoverageGate -- --report …` | `[UNVERIFIED]` — expected exit 0 |

## Known gaps

1. **Batch items are applied one at a time, not atomically.** `InMemoryActivityRepository.UpdateAsync`
   is called per item, so a host crash mid-batch leaves a partially applied handover. Acceptable
   for an in-memory reference build; a real deployment would need the batch inside one transaction
   or an idempotency key on the request. Not in the contract.
2. **No optimistic concurrency.** Two overlapping handovers touching the same activity resolve
   last-writer-wins. `Activity` has no version field; adding one is a change to the base entity and
   therefore its own sprint.
3. **`FindManyAsync` is O(n) lookups against a dictionary**, which is correct here but hides the
   N+1 shape a database repository would have. Flagged so the port is deliberate.
