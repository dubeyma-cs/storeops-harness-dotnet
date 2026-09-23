# Sprint 2 contract — Handover notification via the event bus

**Spec:** [`spec.md`](spec.md)
**Scope:** publishing `ActivitiesBulkStatusAppliedEvent` from the handover path, and the `alerts`
module's reaction to it.
**Modules:** `activities` (publish), `alerts` (subscribe) **Layers:** service, event
**Depends on:** sprint 1

## Acceptance criteria

### AC-13
GIVEN a `TODO` activity in the caller's store
WHEN a department lead applies a handover batch that sets it to `DONE`
THEN every `STORE_MANAGER` in that store has a `SHIFT_HANDOVER` alert whose body names the acting
staff member and reports "1 of 1" updated
Layer: event

### AC-14
GIVEN a handover batch in which every item fails
WHEN it is submitted
THEN no `SHIFT_HANDOVER` alert is created — a handover that changed nothing is not news
Layer: service

### AC-15
GIVEN the whole source tree
WHEN the architecture gate runs
THEN the module dependency graph contains no edge from `activities` to `alerts`, and
`StoreOps.ArchCheck` reports zero `SO-002` violations — the notification is reached only through
the bus
Layer: service

## Definition of done

- [x] every AC above has a named test that asserts the THEN clause
- [x] `dotnet build StoreOps.sln --warnaserror` → exit 0
- [x] `dotnet test StoreOps.sln` → exit 0
- [x] `dotnet run --project tools/StoreOps.ArchCheck` → exit 0
- [x] `dotnet run --project tools/StoreOps.CoverageGate -- --report <cobertura>` → exit 0
- [x] `generator-summary.md` self-check table complete, every test name resolvable

## Explicitly out of scope

- Alerting department leads as well as store managers — open question 2 in `spec.md`.
- `EMAIL` delivery. No transport exists.
- Per-item events. The aggregate event carries the updated ids; a subscriber needing per-activity
  granularity would use the existing `ActivityStatusChangedEvent`, which the handover path already
  publishes once per updated item.

## Design decisions carried by this contract

| Decision | Value | Reasoning |
| --- | --- | --- |
| Recipients | every `STORE_MANAGER` in the store | smallest recipient set that satisfies the Intent; expanding it is open question 2 |
| Event shape | aggregate counts + updated ids | lets `reports` track handover throughput later without reading the activities repository |
| Publication point | once, after all items are resolved and audit rows are written | a subscriber must never observe a half-applied batch |
| Empty-change behaviour | publish the event, do not raise an alert | the event is a fact worth logging; the alert is a judgement about relevance, and that judgement belongs to `alerts` |
