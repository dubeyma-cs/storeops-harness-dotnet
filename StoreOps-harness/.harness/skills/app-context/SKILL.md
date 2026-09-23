# Skill: app-context

**Read by:** all four agents · **Budget:** 1.5 pages — every agent pays for this file, keep it short

## Purpose

What StoreOps is, what its modules own, and what things are called. Read this before anything else
so that a spec, a code change, a review finding and a run log all use the same nouns.

## The application

StoreOps is a REST API for retail store operations: store teams create operational programmes,
assign and track activities across departments, coordinate staff, and read performance reports by
store and region.

- **Stack:** C# 12 / .NET 8, ASP.NET Core Web API, xUnit + `WebApplicationFactory`, StyleCop +
  nullable annotations.
- **Storage:** in-memory, singleton repositories. No database. State survives for the process
  lifetime, which is why integration tests get a fresh host per test class.
- **Auth:** bearer token resolved against a fixed seeded roster by
  `StaffAuthenticationMiddleware` (staff module). Every request to `/api/*` needs one.
- **Scope:** everything is store-scoped. A caller sees their own store's data because the
  repository filters on `StaffContext.StoreId`, not because the caller asked nicely.

## The five modules

| Module | Owns | Key types |
| --- | --- | --- |
| `activities` | operational activities — restocking, planogram resets, audits, compliance checks | `Activity`, `ActivityStatus`, `ActivityPriority`, `ActivityCategory`, `ActivityAuditEntry` |
| `programmes` | store programmes and their staff membership — seasonal rollouts, compliance drives, refits | `Programme`, `ProgrammeMember`, `ProgrammeRole`, `ProgrammeStatus` |
| `staff` | staff registration, authentication, profiles. **No REST surface**; read-only to every other module | `StaffMember`, `StaffProfile`, `StaffRole`, `AuthToken` |
| `alerts` | in-app alerts triggered by operational events | `Notification`, `NotificationChannel`, `NotificationStatus`, `AlertType`, `PendingEscalation` |
| `reports` | store and regional summaries. **Read-only aggregator**; no REST surface in the baseline | `Report`, `ReportType`, `ReportStatus`, `ReportPayload` |

Each module has the same internal shape:

```
src/StoreOps.Api/Modules/<Module>/
  <Module>Controller.cs        routes      — HTTP shaping only
  I<Thing>Service.cs           service     — the module's only public face
  <Thing>Service.cs            service     — business rules
  I<Thing>Repository.cs        repository  — data access contract
  InMemory<Thing>Repository.cs repository  — data access
  Models.cs / Dtos.cs          entities and wire contracts
  <Module>Module.cs            composition root — DI registration
```

`src/StoreOps.Api/Shared/` holds the shared kernel: `Errors/` (the `AppError` hierarchy and the
error middleware), `Events/` (`IEventBus`, `DomainEvent`, every event contract), `Auth/`
(`StaffContext`), `Time/` (`IClock`). **Shared never references a module.**

## Naming map — specification name → code name

The capstone specification names three entities that could not keep their names in C#. Use the
right-hand column everywhere in code, tests and artefacts.

| Specification | Code | Why |
| --- | --- | --- |
| `Task`, `TaskStatus`, `TaskPriority`, `TaskCategory` | `Activity`, `ActivityStatus`, `ActivityPriority`, `ActivityCategory` | `Task` collides with `System.Threading.Tasks.Task` in an async codebase; the module and route are `activities` anyway |
| `Project`, `ProjectMember`, `ProjectRole` | `Programme`, `ProgrammeMember`, `ProgrammeRole` | matches the module name and the `/api/programmes` route |
| `User`, `UserProfile` | `StaffMember`, `StaffProfile` | "user" means nothing on a shop floor; the module is `staff` |

Enum **member** names are kept verbatim from the specification (`TODO`, `IN_PROGRESS`, `DONE`,
`BLOCKED`, `LOW`…`CRITICAL`, `RESTOCKING`…`GENERAL`, `IN_APP`, `EMAIL`, `SLA_BREACH`, …) because
they are on the wire.

## Endpoint inventory

Baseline (the nine required endpoints):

| Method + path | Module | Description |
| --- | --- | --- |
| `GET /api/activities` | activities | list activities (optional `programmeId`, `status` filters) |
| `POST /api/activities` | activities | create an activity |
| `GET /api/activities/{id}` | activities | get activity by id |
| `PATCH /api/activities/{id}` | activities | update status, priority, category, assignee |
| `DELETE /api/activities/{id}` | activities | delete (creator or store management only) |
| `GET /api/programmes` | programmes | list programmes for the authenticated store |
| `POST /api/programmes` | programmes | create a programme |
| `POST /api/programmes/{id}/members` | programmes | add a staff member to a programme |
| `GET /api/alerts` | alerts | alerts for the authenticated staff member |

Deliberate additions beyond the nine, each with a reason:

| Method + path | Why it exists |
| --- | --- |
| `PATCH /api/activities/bulk-status` | the shift-handover feature built by the harness demonstration run |
| `GET /api/activities/{id}/audit` | makes the handover audit trail observable, so its acceptance criteria are testable and demonstrable |
| `POST /api/programmes/{id}/close` | the only trigger for `ProgrammeClosedEvent`, without which the event-bus path to `reports` cannot be exercised |
| `GET /health` | container and App Service health probe; the only anonymous route |

## Seeded roster

Fixed, in `InMemoryStaffRepository.SeedData`. Store `store-042`, region `region-north`.

| Staff | Id | Role | Department | Bearer token |
| --- | --- | --- | --- | --- |
| Priya Raman | `staff-001` | `STORE_MANAGER` | `OPERATIONS` | `dev-token-store-manager` |
| Sam Okafor | `staff-002` | `DEPARTMENT_LEAD` | `GROCERY` | `dev-token-department-lead` |
| Lee Chen | `staff-003` | `ASSOCIATE` | `GROCERY` | `dev-token-associate` |

Use these in tests, in curl walkthroughs and in the deployment evidence. They are not secrets and
not credentials for anything — they exist so that every artefact addresses the same three people.

## Commands

```bash
dotnet build StoreOps.sln --warnaserror
dotnet test StoreOps.sln
dotnet run --project tools/StoreOps.ArchCheck
dotnet run --project tools/StoreOps.CoverageGate -- --report tests/StoreOps.Api.Tests/TestResults/coverage.cobertura.xml
dotnet run --project src/StoreOps.Api          # http://localhost:5000
```
