# Skill: sprint-decomposition

**Read by:** Planner · **Budget:** 2 pages

## Purpose

How to cut a StoreOps feature into sprints, and how to write an acceptance criterion that a test
can verify without interpretation.

## Where to draw a sprint boundary

Apply these in order; the first one that applies decides the cut.

1. **Synchronous contract before asynchronous side effect.** The request/response behaviour of an
   endpoint is one sprint; the events it publishes and what other modules do about them is the
   next. They need different test shapes — one asserts a response body, the other asserts a
   recipient's alert list after an event — and a mixed sprint lets a passing half hide a failing
   half.
2. **One module's write surface per sprint.** A sprint that writes in two modules is either
   violating `SO-002` or is two sprints.
3. **A sprint must end green.** Every check passes at the end of every sprint. If sprint N only
   compiles once N+1 lands, the boundary is wrong.
4. **Split on cardinality, not on files.** "Single item" and "batch of items" are different rule
   sets (partial failure, duplicates, caps) and deserve separate sprints if both are new. Adding
   ten similar DTOs is not a reason to split.
5. **Cap a sprint at roughly six to twelve acceptance criteria.** Fewer means the boundary is
   arbitrary; more means the Generator gets one iteration per three criteria and the feedback file
   becomes unusable.

## Acceptance criterion grammar

```
AC-<n>  GIVEN <concrete starting state>
        WHEN  <one action, as a caller performs it>
        THEN  <observable outcome, as a value a test can read>
        Layer: routes | service | repository | event
```

Ids are stable for the life of the run and never renumbered — the Generator's self-check table, the
Evaluator's per-check results and the run log all key on them.

**A THEN clause must name at least one of:** a status code *plus* a state change; a stored record
(with the field that proves it); a published event (by contract type); a rejected transition (with
the error code). A status code on its own is not an observable outcome — that is failure mode 3
from the client context, written into the contract.

### Testable vs. not

| Rejected | Why | Rewritten |
| --- | --- | --- |
| THEN the request is handled correctly | "correctly" is the thing under dispute | THEN the response is `200` and every item's `outcome` is `UPDATED` |
| THEN errors are handled gracefully | no reader can agree on graceful | THEN the response is `409` and `error.code` is `INVALID_STATE_TRANSITION` |
| THEN an audit record is created | which record, keyed how? | THEN exactly one `ActivityAuditEntry` exists for the activity with `source` = `api.bulk-status`, `fromStatus` = `TODO`, `toStatus` = `BLOCKED` |
| THEN the alerts module is notified | describes a call, not an outcome; invites an `SO-002` violation | THEN an `ActivitiesBulkStatusAppliedEvent` is published, and every `STORE_MANAGER` in the store has a `SHIFT_HANDOVER` alert naming the actor |
| THEN performance is acceptable | unmeasurable here | (out of scope — state it in the spec's *Out of scope*) |

### Criteria every collection endpoint must have

Non-negotiable, because the first draft of the shift-handover endpoint shipped without them:

- maximum collection size, with the rejection behaviour
- duplicate entries in the collection, with the rejection behaviour
- empty collection
- partial failure: what happens to the items that *did* succeed
- the unauthenticated case

## Contract template

```markdown
# Sprint N contract — <feature name>

**Spec:** `.harness/output/spec.md`
**Scope:** <one sentence>
**Modules:** <module list>  **Layers:** <layer list>
**Depends on:** sprint N-1 (or "nothing")

## Acceptance criteria

### AC-1
GIVEN …
WHEN  …
THEN  …
Layer: service

## Definition of done

- [ ] every AC above has a named test that asserts the THEN clause
- [ ] `dotnet build StoreOps.sln --warnaserror` → exit 0
- [ ] `dotnet test StoreOps.sln` → exit 0
- [ ] `dotnet run --project tools/StoreOps.ArchCheck` → exit 0
- [ ] `dotnet run --project tools/StoreOps.CoverageGate -- --report <cobertura>` → exit 0
- [ ] `.harness/output/generator-summary.md` self-check table complete, test names resolvable

## Explicitly out of scope

<list>

## Design decisions carried by this contract

| Decision | Value | Reasoning |
```

The *Design decisions carried by this contract* table is where the Planner records the choices it
proposed rather than leaving them to the Generator: batch caps, grace periods, the status code for
a partial failure. The approval gate is the developer accepting that table.

## Worked example — the demonstration feature

Intent: *"let an outgoing shift mark many activities DONE or BLOCKED in one request, with partial
failure handling and an audit entry per updated activity."*

| Sprint | Scope | Boundary reasoning | ACs |
| --- | --- | --- | --- |
| 1 | `PATCH /api/activities/bulk-status`: per-item validation, partial failure, status-code selection, one audit entry per updated activity | heuristic 1 — this is the synchronous request contract; it is complete and testable without any other module reacting | AC-1 … AC-12 |
| 2 | publish `ActivitiesBulkStatusAppliedEvent`; `alerts` raises a `SHIFT_HANDOVER` notification to store management | heuristic 1 and 2 — the side effect crosses a module boundary, must go through the bus, and is asserted by reading a recipient's alert list rather than a response body | AC-13 … AC-15 |

Note what is *not* a sprint boundary here: the DTOs, the service method and the controller action
all land in sprint 1 together, because they are one behaviour expressed at three layers.
