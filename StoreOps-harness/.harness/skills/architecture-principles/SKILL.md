# Skill: architecture-principles

**Read by:** Planner, Generator, Evaluator · **Budget:** 3 pages

## Purpose

The seven non-negotiable StoreOps architecture rules, each with the code shape that satisfies it,
the code shape that violates it, and how it is checked. Rule codes `SO-001`…`SO-007` are the same
codes `tools/StoreOps.ArchCheck` emits and the same codes the Evaluator and run logs quote.

Each rule traces to a failure mode the client's standards team found in review:

| Failure mode from the client context | Rule |
| --- | --- |
| direct imports from another module's repository | `SO-001`, `SO-007` |
| raw `Error`/`Exception` throws in service methods | `SO-003` |
| tests asserting status codes but not business rules | covered by `how-to-test` + the AC-coverage gate |
| missing event bus integration, writes into sibling modules | `SO-002`, `SO-005` |

---

## SO-001 — Module boundary

**Rule.** No module may reference another module's repository. Cross-module reads go through the
target module's **service** interface only.

**Satisfying shape** — activities needs to know whether an assignee exists:

```csharp
// ActivityService.cs
private readonly IStaffService _staffService;   // another module's SERVICE

var assignee = await _staffService.FindAsync(assigneeStaffId, cancellationToken);
```

**Violating shape:**

```csharp
private readonly IStaffRepository _staffRepository;   // SO-001: another module's REPOSITORY
```

**Why it matters.** The service layer is where store scoping, activity status and role rules live.
A caller that reaches the repository directly gets rows with none of those rules applied, and the
owning module can no longer change its storage without breaking a module it has never heard of.

**Checked by.** `StoreOps.ArchCheck` — repository-type ownership is derived from where the type is
declared, so the rule keeps working for repositories that do not exist yet.

---

## SO-002 — Event bus only

**Rule.** A side effect that crosses a module boundary must be raised through `IEventBus`. A module
must not depend on a downstream side-effect module's service. `alerts` and `reports` are
side-effect modules: nothing may call into them directly.

**Satisfying shape** — a handover must notify store management:

```csharp
// ActivityService.cs — activities publishes and does not care who listens
await _eventBus.PublishAsync(
    new ActivitiesBulkStatusAppliedEvent(storeId, actorStaffId, requested, updated, failed, ids),
    cancellationToken);

// AlertsEventSubscriptions.cs — alerts listens and activities never learns about it
On<ActivitiesBulkStatusAppliedEvent>((services, applied, token) =>
    services.GetRequiredService<IAlertService>().HandleBulkStatusAppliedAsync(applied, token));
```

**Violating shape:**

```csharp
// ActivityService.cs
private readonly IAlertService _alerts;               // SO-002
await _alerts.NotifyHandoverAsync(storeId, count);     // SO-002
```

**Why it matters.** Without it, `activities` cannot be tested, deployed or reasoned about without
`alerts`, and a failure in alert delivery fails the shift-handover request that a store colleague
is waiting on. The bus swallows and logs subscriber failures precisely so that cannot happen.

**Two supporting design decisions, both load-bearing:**

1. Event contracts live in `Shared/Events/Contracts.cs`, not in the publishing module. If
   `ActivityStatusChangedEvent` lived in `Modules/Activities`, every subscriber would have to
   import `activities` to read it — reintroducing the coupling the bus removes.
2. Event payloads carry enum values as **strings**. `alerts` reacts to `"CRITICAL"` without
   importing `ActivityPriority`.

**Checked by.** `StoreOps.ArchCheck` (automated) plus an Evaluator judgement check on whether a new
cross-module trigger was routed through the bus at all.

---

## SO-003 — Error contract

**Rule.** No raw exception throws in services or routes. Every failure uses the `AppError`
hierarchy, which carries `Code`, `Message` and `StatusCode`.

**Satisfying shape:**

```csharp
throw new NotFoundError("Activity", activityId);
throw new InvalidStateTransitionError(existing.Status.ToString(), target.ToString());
throw ValidationError.ForField("reason", "A reason is required when setting status to BLOCKED.");
```

**Violating shape:**

```csharp
throw new Exception($"activity {id} not found");          // SO-003
throw new InvalidOperationException("bad transition");     // SO-003
throw new KeyNotFoundException();                          // SO-003
```

The available errors are `ValidationError`, `NotFoundError`, `ConflictError`,
`InvalidStateTransitionError`, `ForbiddenError`, `UnauthorizedError`. Codes are in `ErrorCodes`;
they are on the wire, so they are additive-only and never renamed.

**Why it matters.** `AppErrorHandlingMiddleware` is the one place that maps an error to HTTP. A raw
throw reaches it as an unhandled exception and becomes a `500 INTERNAL_ERROR` — a client-visible
lie about whose fault it was, and an unactionable log entry.

**Guard-clause exception.** `ArgumentNullException.ThrowIfNull(x)` on a public method's parameters
is allowed; it is a programming-error assertion, not a business failure, and it is not a
`throw new`.

**Checked by.** `StoreOps.ArchCheck` scans service and route files for `throw new <Type>` where the
type name does not end in `Error`. Comments are stripped before scanning, so documenting a banned
pattern is safe.

---

## SO-004 — Layer separation

**Rule.** Routes → Service → Repository, no skipping.

| Layer | Must | Must not |
| --- | --- | --- |
| Routes (`*Controller.cs`) | bind and shape-validate, call one service method, map the result to a status code | contain a business rule, reference a repository, catch an `AppError` |
| Service (`*Service.cs`) | own every business rule, publish events, decide errors | reference `HttpContext`, build responses, reach into another module's repository |
| Repository (`*Repository.cs`) | filter, persist, return | reference a service, `IEventBus`, `IStaffContextAccessor`, or any HTTP type |

**Satisfying shape** — the bulk-status action picks a status code from counts alone:

```csharp
var statusCode = (result.Updated, result.Failed) switch
{
    (_, 0) => StatusCodes.Status200OK,
    (0, _) => StatusCodes.Status409Conflict,
    _      => StatusCodes.Status207MultiStatus,
};
```

Nothing about an activity is inspected in the controller, so the rule about which statuses are
legal cannot drift between the route and the service.

**Why it matters.** Business rules in a controller are unreachable from the background workers and
the event handlers that also need them, so they get copied — and then the two copies disagree. The
handover path and the single-`PATCH` path share `ActivityStatusPolicy` for exactly this reason.

**Checked by.** `StoreOps.ArchCheck` for the structural half (controllers referencing repositories,
repositories referencing services or HTTP types); Evaluator judgement for "is there a business rule
in this controller?", using the recipe in `how-to-review`.

---

## SO-005 — Read-only reports

**Rule.** The `reports` module aggregates `activities`, `programmes` and `staff`, and never writes
to them. It persists only its own `Report` records.

**Satisfying shape:**

```csharp
var activities = await _activityService.ListForStoreAsync(storeId, cancellationToken);
var programmes = await _programmeService.ListForStoreAsync(storeId, cancellationToken);
var staff      = await _staffService.ListForStoreAsync(storeId, cancellationToken);
```

**Violating shape:**

```csharp
await _activityService.UpdateAsync(id, request, cancellationToken);   // SO-005
```

**Why it matters.** A report that writes is no longer a report; it is a second, invisible author of
activity state, and the completion rate it publishes becomes a number nobody can reconcile.

**Checked by.** `StoreOps.ArchCheck` — every call from a `reports` file to another module's service
must invoke a read verb (`List`, `Get`, `Find`, `Count`, `Query`, `Exists`, `Authenticate`).
Reinforced in the type system: cross-module reads return `ActivityReadModel` /
`ProgrammeReadModel` projections, not entities.

---

## SO-006 — Shared independence

**Rule.** `Shared/` must not depend on any module. Modules depend on `Shared`, never the reverse.

**Why it matters.** The moment `Shared` imports `activities`, it stops being a kernel and becomes a
cycle with five participants. This is also why `StaffAuthenticationMiddleware` lives in
`Modules/Staff/` and not in `Shared/Auth/`: it needs `IStaffService`, and authentication is the
staff module's stated responsibility.

**Checked by.** `StoreOps.ArchCheck`.

---

## SO-007 — No circular module dependencies

**Rule.** The module dependency graph stays acyclic.

The intended graph, which `StoreOps.ArchCheck` prints on every run:

```
activities -> staff
alerts     -> activities, staff
programmes -> staff
reports    -> activities, programmes, staff
staff      -> (none)
```

`staff` depends on nothing, because it is read-only to everyone. `alerts` and `reports` sit
downstream and are reached only through the bus.

**Checked by.** `StoreOps.ArchCheck` — depth-first cycle detection over the derived graph.

---

## Store scoping (not a numbered rule, but gated in review)

Every read and every write is scoped by `StaffContext.StoreId`, enforced in the repository, not the
caller. `FindAsync(id, storeId)` returns `null` for an activity in another store — a 404, not a
403, because a caller must not learn that an id exists elsewhere.

Background work (`SlaSweepWorker`, `EscalationSweepWorker`) and event handlers run under a named
system identity from `SystemIdentity` / `AlertsEventSubscriptions.Identity` rather than with no
identity, so the audit trail always has an actor.
