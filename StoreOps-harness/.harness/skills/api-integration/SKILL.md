# Skill: api-integration

**Read by:** Generator · **Budget:** 2.5 pages

## Purpose

How a StoreOps endpoint is wired from route to response: status-code selection, the error envelope,
validation split, store scoping, and event publication. This is the file that keeps a new endpoint
indistinguishable in shape from the nine that already exist.

## Route conventions

```csharp
[ApiController]
[Route("api/activities")]        // plural, lower-case, matches the module name
[Produces("application/json")]
public sealed class ActivitiesController : ControllerBase
```

- One controller per module; the route prefix is the module name, pluralised
- Sub-resources nest: `POST /api/programmes/{id}/members`, `GET /api/activities/{id}/audit`
- Action verbs live in the HTTP method, not the path. The exceptions are deliberate and few:
  `PATCH /api/activities/bulk-status` (a batch operation on the collection) and
  `POST /api/programmes/{id}/close` (a state transition that is not a field update)
- A literal segment outranks `{id}` in ASP.NET Core route precedence, so `bulk-status` binds to the
  bulk action and never to `PATCH /api/activities/{id}` — but write a test that proves it anyway
  (`AC-12` exists for this)
- Every action takes `CancellationToken cancellationToken` last and passes it on
- Declare `[ProducesResponseType]` for every status code the action can return, including errors —
  it is the Swagger contract and it documents the intent for the Evaluator

## Status-code selection

| Situation | Status | Body |
| --- | --- | --- |
| Read succeeded | `200` | the resource or a list |
| Resource created | `201` + `CreatedAtAction` | the created resource |
| Command succeeded, nothing to return | `204` | empty |
| Batch: every item succeeded | `200` | the batch result envelope |
| Batch: some items succeeded | `207` | the batch result envelope |
| Batch: every item failed | `409` | the batch result envelope |
| Shape validation failed | `400` | error envelope, `VALIDATION_FAILED`, `details` per field |
| Business validation failed | `400` | error envelope, `VALIDATION_FAILED` |
| No or bad bearer token | `401` | error envelope, `UNAUTHENTICATED` |
| Role or ownership check failed | `403` | error envelope, `OPERATION_FORBIDDEN` |
| Unknown id, or id in another store | `404` | error envelope, `RESOURCE_NOT_FOUND` |
| Conflicts with current state | `409` | error envelope, `RESOURCE_CONFLICT` |
| Illegal lifecycle transition | `409` | error envelope, `INVALID_STATE_TRANSITION` |

**The batch rule in code.** Derive the code from counts only — never by inspecting an item:

```csharp
var statusCode = (result.Updated, result.Failed) switch
{
    (_, 0) => StatusCodes.Status200OK,
    (0, _) => StatusCodes.Status409Conflict,
    _      => StatusCodes.Status207MultiStatus,
};
return StatusCode(statusCode, result);
```

An id in another store is a `404`, not a `403`. A caller must not be able to probe whether an id
exists in a store they cannot see.

## The error envelope

Every error response, from every route, has one shape:

```json
{
  "error": {
    "code": "INVALID_STATE_TRANSITION",
    "message": "Transition from 'DONE' to 'IN_PROGRESS' is not permitted.",
    "statusCode": 409,
    "traceId": "0HN7…",
    "details": { "reason": ["A reason is required when setting status to BLOCKED."] }
  }
}
```

- Produced only by `AppErrorHandlingMiddleware`. A controller never builds an error body, never
  catches an `AppError`, and never returns `BadRequest(...)` with an ad-hoc object
- `details` is present only for field-level validation failures
- Framework model-binding failures are funnelled into the same envelope by the
  `InvalidModelStateResponseFactory` in `Program.cs`, which throws a `ValidationError` rather than
  letting ASP.NET emit `ProblemDetails`. There is exactly one error shape on the wire, and
  `ErrorContractTests` asserts that `"title"` never appears in an error body

## Validation split

| Kind | Where | How | Failure |
| --- | --- | --- | --- |
| Shape — required, length, range, collection size | request DTO, `DataAnnotations` | `[Required]`, `[StringLength]`, `[MinLength]`, `[MaxLength]` | `400`, `details` keyed by property name |
| Cross-field — "end after start" | service | explicit check | `ValidationError.ForField` |
| Referential — "this assignee is store staff" | service, via another module's **service** | `IStaffService.FindAsync` | `ValidationError.ForField` |
| Lifecycle — "DONE is terminal" | service, via a policy type | `ActivityStatusPolicy.CanTransition` | `InvalidStateTransitionError` → `409` |
| Authorisation — "creator or store management" | service, from `StaffContext` | `caller.IsStoreManagement` | `ForbiddenError` → `403` |

Shape validation in the DTO, meaning in the service. A `MaxLength(100)` on the batch belongs on the
DTO; "only DONE or BLOCKED during handover" belongs in `ActivityStatusPolicy`.

## Store scoping

The service reads the caller from `IStaffContextAccessor` and passes `StoreId` into every
repository call:

```csharp
private StaffContext Caller =>
    _staffContext.Current
    ?? throw new UnauthorizedError("No authenticated staff identity on this request.");

var activity = await _repository.FindAsync(activityId, Caller.StoreId, cancellationToken);
```

A repository method that takes an id without a store id is a bug waiting to happen; the only
exceptions are the append-only audit reads, which are reached through a scoped `GetAsync` first.

## Publishing events

Publish **after** the state change has been persisted, once per accepted change, using a contract
from `Shared/Events/Contracts.cs`:

```csharp
await _eventBus.PublishAsync(
    new ActivityStatusChangedEvent(
        after.Id, after.StoreId, before.Status.ToString(), after.Status.ToString(),
        after.Priority.ToString(), after.Category.ToString(), after.AssigneeStaffId,
        actorStaffId, reason),
    cancellationToken);
```

Adding an event is three edits and no more: the record in `Contracts.cs`, the `PublishAsync` call
in the owning service, the `On<TEvent>` line in the consuming module's `*EventSubscriptions.cs`.
If a fourth file needs to change, something is crossing a boundary it should not.

Payload rules: identifiers and enum-values-as-strings only — no entities, no DTOs, no
`ActivityPriority`. A subscriber must never need to import the publishing module to read an event.

Subscriber failures are logged and swallowed by `InMemoryEventBus`, so a broken alert handler
cannot fail the handover request. That means a publisher must not rely on a subscriber's work
having happened — if a caller needs the result, it is not a side effect and does not belong on the
bus.

## Batch endpoint checklist

Every collection-accepting endpoint needs all of these, because the first draft of the handover
endpoint had none:

- [ ] `MaxItems` constant on the request DTO, enforced by `[MaxLength]` *and* re-checked in the
      service (the DTO guards the wire; the service guards every caller)
- [ ] `[MinLength(1)]` — an empty batch is a client bug, not a no-op
- [ ] duplicate-id rejection for the **whole** request: ambiguous input must not be resolved by
      iteration order
- [ ] per-item result records carrying `activityId`, `outcome`, and on failure `errorCode` and
      `message` drawn from the same `ErrorCodes` constants as the envelope
- [ ] one load of all referenced entities (`FindManyAsync`), not N round trips
- [ ] one audit row per **successfully** changed item, and none for failed items
- [ ] one aggregate event for the batch, plus one per-item event if per-item subscribers exist
