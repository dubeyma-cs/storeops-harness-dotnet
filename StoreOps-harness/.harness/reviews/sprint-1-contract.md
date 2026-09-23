# Sprint 1 contract — Shift handover bulk status update

**Spec:** [`spec.md`](spec.md)
**Scope:** the synchronous request contract of `PATCH /api/activities/bulk-status` — per-item
validation, partial-failure resolution, status-code selection, and one audit entry per updated
activity.
**Modules:** `activities` **Layers:** routes, service, repository
**Depends on:** nothing

## Acceptance criteria

### AC-1
GIVEN two `TODO` activities in the caller's store
WHEN the caller submits a handover batch setting one to `DONE` and one to `BLOCKED` with a reason
THEN the response is `200`, `updated` is 2, `failed` is 0, every item's `outcome` is `UPDATED`, the
first activity reads back as `DONE` with a `completedAt`, and the second reads back as `BLOCKED`
with no `completedAt`
Layer: service

### AC-2
GIVEN one `TODO` activity, one `DONE` activity, and one id that does not exist
WHEN the caller submits a handover batch setting all three to `DONE`
THEN the response is `207`, `updated` is 1, `failed` is 2, the `DONE` activity's item carries
`errorCode` `INVALID_STATE_TRANSITION`, the unknown id's item carries `RESOURCE_NOT_FOUND`, and the
`TODO` activity reads back as `DONE`
Layer: service

### AC-3
GIVEN a `DONE` activity and an id that does not exist
WHEN the caller submits a handover batch containing only those two
THEN the response is `409`, `updated` is 0, `failed` is 2, and no activity changed
Layer: routes

### AC-4
GIVEN a `TODO` activity
WHEN the caller sets it to `BLOCKED` through the handover batch with reason "Chiller fault raised
with maintenance"
THEN exactly one `ActivityAuditEntry` exists for that activity with `source` `api.bulk-status`,
`action` `STATUS_CHANGED`, `fromStatus` `TODO`, `toStatus` `BLOCKED`, that reason, and
`actorStaffId` equal to the calling staff member
Layer: repository

### AC-5
GIVEN a `DONE` activity
WHEN a handover batch attempts to set it to `DONE` again
THEN the audit row count for that activity is unchanged and no row with `source` `api.bulk-status`
exists for it
Layer: service

### AC-6
GIVEN a `TODO` activity
WHEN a handover batch item requests status `IN_PROGRESS`
THEN that item fails with `errorCode` `VALIDATION_FAILED`, the message names the permitted statuses,
and the activity is still `TODO`
Layer: service

### AC-7
GIVEN a `TODO` activity
WHEN a handover batch item requests `BLOCKED` with no reason
THEN that item fails with `errorCode` `VALIDATION_FAILED` and a message naming the missing reason
Layer: service

### AC-8
GIVEN a `TODO` activity
WHEN a handover batch contains the same activity id twice
THEN the whole request is rejected with `400` and `error.code` `VALIDATION_FAILED`, and the activity
is still `TODO`
Layer: service

### AC-9
GIVEN any caller
WHEN a handover batch is submitted with an empty `items` array
THEN the response is `400`
Layer: routes

### AC-10
GIVEN any caller
WHEN a handover batch is submitted with 101 items
THEN the response is `400`
Layer: routes

### AC-11
GIVEN a request with no `Authorization` header
WHEN a handover batch is submitted
THEN the response is `401` and `error.code` is `UNAUTHENTICATED`
Layer: routes

### AC-12
GIVEN a well-formed handover batch whose single item references an unknown activity
WHEN it is submitted to `PATCH /api/activities/bulk-status`
THEN the response is the batch envelope with `requested` 1 — proving the literal route segment was
not bound as an activity id by `PATCH /api/activities/{id}`
Layer: routes

## Definition of done

- [x] every AC above has a named test that asserts the THEN clause
- [x] `dotnet build StoreOps.sln --warnaserror` → exit 0
- [x] `dotnet test StoreOps.sln` → exit 0
- [x] `dotnet run --project tools/StoreOps.ArchCheck` → exit 0
- [x] `dotnet run --project tools/StoreOps.CoverageGate -- --report <cobertura>` → exit 0
- [x] `generator-summary.md` self-check table complete, every test name resolvable

## Explicitly out of scope

- The `SHIFT_HANDOVER` alert and the `ActivitiesBulkStatusAppliedEvent` publication — sprint 2.
- Changing any field other than `status` in a batch.
- Any change to `PATCH /api/activities/{id}`.

## Design decisions carried by this contract

| Decision | Value | Reasoning |
| --- | --- | --- |
| Partial success status | `207` | processed, items disagree — see `spec.md` |
| All failed status | `409` | nothing changed, cause is state |
| `MaxItems` | 100 | bounds the request; enforced on the DTO and re-checked in the service |
| Duplicate ids | whole-request `400` | ambiguous input must not be resolved by ordering |
| Handover statuses | `DONE`, `BLOCKED` | held in `ActivityStatusPolicy.HandoverStatuses`, shared with the single-activity path |
| Per-item failures | data, not exceptions | partial success is impossible if the first bad item throws |
