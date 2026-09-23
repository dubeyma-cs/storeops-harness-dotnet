# Spec — Shift handover bulk status update

**Run:** 2026-09-18 · **Planner:** Claude Code (planner.agent.md) · **Prompt:** [`PROMPT.md`](../../PROMPT.md)

## Intent restated

When a shift ends, the outgoing team needs to close out or flag the activities they worked on in
one action instead of one request per activity. They must be able to mark many activities `DONE` or
`BLOCKED` together; activities that cannot be changed must not block the ones that can; and every
change must leave an audit record naming who made it, when, and why. Store management needs to know
a handover happened.

One ambiguity in the Intent, named rather than silently resolved: *"mark multiple activities as
DONE or BLOCKED"* does not say whether a mixed outcome is a success or a failure for the request as
a whole. Resolved by the design decision table below (207 Multi-Status), carried through the
approval gate.

## Affected modules

| Module | Change | Layer(s) touched |
| --- | --- | --- |
| `activities` | new batch endpoint, new service method, per-item validation, audit rows, new event | routes, service, repository |
| `alerts` | new subscription raising a `SHIFT_HANDOVER` notification | service, event subscription |
| `staff` | none | — |
| `programmes` | none | — |
| `reports` | none | — |

## Out of scope

- Undoing a handover. A batch is not a transaction and there is no "unhandover" operation.
- Changing priority, category or assignee in a batch. Handover is about status only; a reviewer
  looking for those fields will not find them.
- Any change to `PATCH /api/activities/{id}` behaviour.
- Notifying the incoming shift by email. `NotificationChannel.EMAIL` exists but no transport does;
  `IN_APP` only.
- Cross-store batches. Every item is resolved within the caller's store scope.

## Architectural impact

| Rule | How this feature satisfies it |
| --- | --- |
| `SO-001` module boundary | the batch path reads and writes only `IActivityRepository`; the assignee/lead lookups it needs go through `IStaffService` |
| `SO-002` event bus only | the handover notification is raised by publishing `ActivitiesBulkStatusAppliedEvent`; `activities` gains no reference to `IAlertService` |
| `SO-003` error contract | whole-request failures throw `ValidationError`; per-item failures are *data*, carrying an `ErrorCodes` value in the result row rather than an exception |
| `SO-004` layer separation | the controller selects 200/207/409 from counts only; which statuses a handover may set lives in `ActivityStatusPolicy` |
| `SO-005` read-only reports | untouched |
| `SO-006` shared independence | the new event contract goes in `Shared/Events/Contracts.cs` with string enum values, so `alerts` need not import `activities` |
| `SO-007` no cycles | new edge is `alerts → activities` only, which already exists; graph stays acyclic |

A note on `SO-003`: per-item failures deliberately are **not** exceptions. Throwing on the first
bad item would make partial success impossible, so the item result row carries the same
`ErrorCodes` constants the envelope would have used. The error *vocabulary* is shared even though
the transport is not.

## Sprint decomposition

| Sprint | Scope | Why the boundary is here | Acceptance criteria |
| --- | --- | --- | --- |
| 1 | `PATCH /api/activities/bulk-status`: request contract, per-item validation, partial-failure resolution, status-code selection, one audit entry per updated activity | `sprint-decomposition` heuristic 1 — this is the synchronous request/response contract. It is complete and fully testable with no other module reacting, and its tests assert a response body plus persisted state | AC-1 … AC-12 |
| 2 | publish `ActivitiesBulkStatusAppliedEvent`; `alerts` raises a `SHIFT_HANDOVER` notification to every `STORE_MANAGER` in the store | heuristics 1 and 2 — the side effect crosses a module boundary, must go through the bus, and is asserted by reading a recipient's alert list. Different test shape, different module's write surface | AC-13 … AC-15 |

What is deliberately *not* a boundary: the DTOs, the service method and the controller action all
land in sprint 1 together. They are one behaviour expressed at three layers, and splitting them
would leave sprint 1 unable to end green.

## Design decisions carried by this spec

| Decision | Value | Reasoning |
| --- | --- | --- |
| Mixed outcome status code | `207 Multi-Status` | the request was processed; the items disagree. `200` would hide failures from a client that only checks the status; `400` would imply nothing happened |
| All-items-failed status code | `409 Conflict` | nothing changed and the cause is state, not request shape |
| Maximum batch size | 100 | a shift handover at one store is tens of activities; 100 is generous and bounds the request. Enforced on the DTO *and* in the service |
| Duplicate ids | reject the whole request, `400` | two instructions for one activity is ambiguous input; resolving it by iteration order would make the outcome depend on JSON ordering |
| Statuses a handover may set | `DONE`, `BLOCKED` only | an outgoing shift closes work or flags it. Moving an activity back to `TODO`/`IN_PROGRESS` is the incoming shift's decision, through the single-activity route |
| `BLOCKED` requires a reason | yes, per item | an unexplained blocked activity is unactionable for the incoming shift, which defeats the purpose of the handover trail |
| Audit rows for failed items | none | an audit trail records what happened, not what was attempted. The response carries attempt outcomes |

## Open questions

Neither blocks sprint 1.

1. Should a handover batch be restricted to activities assigned to the outgoing staff member, or to
   any activity in the store? Sprint 1 implements store-wide, because a shift covers a department
   rather than a person's own list. Flagged for the client's operations lead.
2. Should `SHIFT_HANDOVER` alerts also go to department leads, not only store managers? Sprint 2
   implements store managers only, to keep the recipient set small and testable.

STATUS: APPROVED 2026-09-18T09:41:00Z
