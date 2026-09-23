# Skill: coding-conventions

**Read by:** Generator · **Budget:** 3 pages

## Purpose

How StoreOps C# is written, and which of those conventions are enforced by the build rather than by
opinion. `dotnet build StoreOps.sln --warnaserror` is a hard gate: in this repository a warning is
an error, so a convention below that is analyser-backed will stop the loop.

## Build policy

`Directory.Build.props` applies to every project and is not overridable per project:

| Property | Value | Consequence |
| --- | --- | --- |
| `LangVersion` | `12.0` | collection expressions, primary constructors available; do not target newer syntax |
| `Nullable` | `enable` | a nullable warning is a build error |
| `ImplicitUsings` | `enable` | do not add `using System;` and friends |
| `TreatWarningsAsErrors` | `true` | unused variable, unreachable code, obsolete API all fail the build |
| `EnforceCodeStyleInBuild` | `true` | `.editorconfig` style rules are compile-time |
| `GenerateDocumentationFile` | `false` | so `CS1591` does not demand XML docs on internals |

`StyleCop.Analyzers` is referenced from `Directory.Build.props`. The ruleset is in
`.editorconfig`, where **every suppression carries a one-line reason**. Two categories are
suppressed and it is worth knowing why before you fight them:

- *Documentation-mandating rules* (`SA1600`–`SA1602`, `SA1633`): documented public API surface is
  required by review, not by the analyser, so internals stay uncluttered.
- *Rules that predate modern C#* (`SA1206` vs. `required` members, `SA1008`/`SA1009` vs. pattern
  and nullable syntax, `SA1101` `this.`, `SA1309` `_field`, `SA1200` with file-scoped namespaces).

**Do not add a suppression to pass a gate.** Editing `.editorconfig` to silence a finding is an
automatic FAIL (`generator.agent.md` rule 6). If a rule is genuinely wrong for StoreOps, record it
under *Known gaps* and let it escalate to a human.

## File and type conventions

- File-scoped namespaces: `namespace StoreOps.Api.Modules.Activities;`
- One public type per file, **except** `Models.cs` and `Dtos.cs`, which group a module's entities
  and wire contracts (`SA1402`/`SA1649` suppressed for this reason)
- Classes are `sealed` unless designed for inheritance (`AppError`, `EventSubscriberService`)
- `internal` is not used to hide things from other modules — `SO-001` does that structurally
- Ordering within a file: constants, fields, constructor, public properties, public methods,
  private helpers

## Naming

| Thing | Convention | Example |
| --- | --- | --- |
| Interface | `I` + noun | `IActivityRepository` |
| Service impl | `<Entity>Service` | `ActivityService` |
| Repository impl | `InMemory<Entity>Repository` | `InMemoryActivityRepository` |
| Controller | `<Module>Controller`, plural module | `ActivitiesController` |
| Request DTO | `<Verb><Entity>Request` | `CreateActivityRequest`, `BulkStatusUpdateRequest` |
| Response DTO | `<Entity>Response` | `ActivityResponse` |
| Cross-module projection | `<Entity>ReadModel` | `ActivityReadModel` |
| Event contract | `<Subject><PastTenseVerb>Event` | `ActivitiesBulkStatusAppliedEvent` |
| Error type | `<Condition>Error` | `InvalidStateTransitionError` |
| Options | `<Module>Options` with `SectionName` | `AlertOptions.SectionName` |
| Async method | `…Async`, returns `Task`/`Task<T>` | `BulkUpdateStatusAsync` |

Enum members that appear on the wire are `SCREAMING_SNAKE_CASE` verbatim from the specification
(`IN_PROGRESS`, `SLA_BREACH`, `STORE_SUMMARY`). This is a deliberate break from .NET convention:
the wire contract wins over `PascalCase`.

## Entities and DTOs

Entities are `sealed record` with `required init` properties, mutated with `with`:

```csharp
var updated = existing with
{
    Status = item.Status,
    CompletedAt = item.Status == ActivityStatus.DONE ? now : null,
    UpdatedAt = now,
};
```

Never mutate an entity in place. The repository takes the new instance; immutability is what makes
the bulk path safe to reason about when half its items fail.

Request DTOs are `sealed class` with `DataAnnotations` for **shape** validation only — length,
range, required, collection size. Business validation lives in the service. Response DTOs are
`sealed record` with a static `From(entity)` projection so the mapping has exactly one home.

## Async and cancellation

- Every I/O-shaped method is async and takes `CancellationToken cancellationToken = default` last
- Pass the token down every call; never pass `CancellationToken.None`
- No `.Result`, no `.Wait()`, no `async void`
- `ConfigureAwait` is not used (`CA2007` suppressed — ASP.NET Core has no synchronisation context)

## Time

Never call `DateTimeOffset.UtcNow` in a service. Inject `IClock` and read `_clock.UtcNow` once per
operation, reusing the value so every record written by one request shares a timestamp. The SLA and
escalation rules are time-dependent, and `how-to-test` requires them asserted with a fixed clock.

## Dependency injection

Each module owns its registrations in `<Module>Module.cs`; `Program.cs` calls the extension method
and never reaches inside a module.

| Lifetime | Used for | Why |
| --- | --- | --- |
| Singleton | repositories, `IEventBus`, `IClock` | in-memory state must survive requests |
| Scoped | services, `IStaffContextAccessor` | per-request store scope and identity |
| Hosted | `EventSubscriberService` subclasses, sweep workers | subscriptions must exist before the first request |

Background work and event handlers create their own scope and set an explicit system identity —
`SystemIdentity.SlaSweep`, `AlertsEventSubscriptions.Identity`. Never leave the identity unset.

## Logging

Structured templates with named placeholders, never interpolation:

```csharp
_logger.LogInformation(
    "Shift handover by {StaffId} on {StoreId}: {Updated} updated, {Failed} failed of {Requested}",
    caller.StaffId, caller.StoreId, updatedCount, failedCount, request.Items.Count);
```

Levels: `Information` for accepted business events, `Warning` for handled `AppError`s and for
"nobody to notify" situations, `Error` only for defects. Never log a bearer token.

## Comments

Comment the decision, not the mechanism. `// increment the counter` is noise; `// Partial-failure
contract: one bad id must not roll back the items that succeeded` is the reason a reviewer needs
six months from now. XML docs on public types and members that another module or a test will call;
`<remarks>` is the right place for a rule and its rationale.
