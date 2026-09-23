# Skill: how-to-test

**Read by:** Generator · **Budget:** 2.5 pages

## Purpose

What a StoreOps test must assert, how to name it, and how to keep a time-dependent or event-driven
rule deterministic. The single rule behind all of it: **a test that asserts only a status code does
not verify a business rule** — that was failure mode 3 in the client context, and the Evaluator has
a dimension for it.

## Naming

```
Given_<starting state>_When_<action>_Then_<observable outcome>
```

The name is the acceptance criterion, in code. It is also how the AC-coverage hard gate works: the
Generator's self-check table names the fully qualified test per AC, and the Evaluator resolves
every name against `dotnet test --list-tests`. Mark the AC in a comment above the test as well —
`// AC-4` — so a reader of the file can trace it without the summary.

Underscored method names fail `CA1707` by default; the test-project section of `.editorconfig`
suppresses it for `tests/**`.

## Which kind of test

| Behaviour | Test kind | Why |
| --- | --- | --- |
| A rule with no dependencies (transitions, overdue, SLA tracking) | plain unit test on the policy or entity | fast, and it names the rule rather than a route |
| An endpoint's contract (status code, body, state change) | integration test through `StoreOpsApiFactory` | asserts the real pipeline: middleware order, auth, error envelope, DI |
| A cross-module reaction | integration test that publishes by calling the API, then reads the consumer's surface | proves the bus path without asserting a call |
| The shared kernel (`InMemoryEventBus`, error envelope) | unit test | contract of the kernel itself |
| An architecture rule | test over `ArchitectureScanner` in `tests/.../Architecture/` | the gate has to be trustworthy in both directions |

Do not mock a repository to test a service. The in-memory repositories *are* the fast
implementation; a mock would assert your own expectation of the repository rather than the rule.

## The test host

```csharp
public sealed class ActivitiesEndpointsTests : IClassFixture<StoreOpsApiFactory>
```

`StoreOpsApiFactory` substitutes exactly two things and nothing else:

1. `IClock` → `FixedClock`, so time-dependent rules are asserted by advancing a clock
2. the two background sweeps are switched off (`Activities:EnableBackgroundSlaSweep=false`,
   `Alerts:EnableBackgroundEscalationSweep=false`), so nothing happens that the test did not ask
   for

Everything else is production wiring. Authenticate with the seeded roster helpers:
`factory.AsStoreManager()`, `.AsDepartmentLead()`, `.AsAssociate()`, or `.WithToken("…")` for the
bad-token cases.

### Fixture sharing and isolation — get this right

Repositories are singletons, so a class fixture shares state across the tests in that class.

- **Share a fixture** when every assertion is scoped to an id the test created
  (`ActivitiesEndpointsTests`, `BulkStatusHandoverTests`).
- **Build a host per test** — `using var factory = new StoreOpsApiFactory();` — when the assertion
  is about a **store-wide count**: the SLA sweep, the escalation sweep and report aggregates all
  see every activity in the store, so a leftover from a sibling test changes the expected number
  and the suite starts depending on execution order. `SlaAlertFlowTests` and
  `ReportEventFlowTests` do this deliberately.

## Driving time

```csharp
var activity = await manager.CreateActivityAsync(
    priority: ActivityPriority.CRITICAL,
    dueAt: factory.Clock.UtcNow.AddHours(2));

factory.Clock.Advance(TimeSpan.FromHours(3));

var flagged = await factory.RunAsync(s =>
    s.GetRequiredService<IActivityService>().SweepSlaBreachesAsync());

Assert.Equal(1, flagged);
```

Never `Task.Delay`, never `Thread.Sleep`, never wait for a background timer. `ScopeRunner.RunAsync`
gives the sweep its own DI scope and a system identity, exactly as the worker does in production.

Assert the boundary, not just the middle: at the due instant it is not yet overdue, one minute
later it is. Off-by-one at a deadline is the bug that reaches a store.

## What an endpoint test must assert

For every write endpoint, all four:

1. **the status code**
2. **the response body** — the field that carries the outcome
3. **the persisted state** — re-read the resource and assert it changed, or did not
4. **the side effect** — the audit trail, or the recipient's alert list

```csharp
Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);          // 1

var body = await response.ReadAsync<BulkStatusUpdateResponse>();
Assert.Equal(1, body.Updated);
Assert.Equal(2, body.Failed);                                            // 2

var after = await (await client.GetAsync($"/api/activities/{valid.Id}"))
    .ReadAsync<ActivityResponse>();
Assert.Equal(nameof(ActivityStatus.DONE), after.Status);                 // 3

var audit = await (await client.GetAsync($"/api/activities/{valid.Id}/audit"))
    .ReadAsync<List<ActivityAuditEntry>>();
Assert.Single(audit.Where(e => e.Source == "api.bulk-status"));          // 4
```

Assert the **negative** side too, and it is usually the more valuable half: a rejected request must
leave state untouched, and a failed batch item must leave no audit row.

## Error assertions

Read the envelope and assert `error.code`, never just the status:

```csharp
var error = await response.ReadAsync<ErrorResponse>();
Assert.Equal(ErrorCodes.InvalidStateTransition, error.Error.Code);
Assert.Contains("reason", error.Error.Details!.Keys);
```

Two different rules can both return `409`; the code is what distinguishes them, and the code is
what a client branches on.

## Event-driven assertions

Assert the **outcome**, not the publication:

```csharp
await lead.PatchJsonAsync("/api/activities/bulk-status", request);

var alerts = await (await manager.GetAsync("/api/alerts")).ReadAsync<List<AlertResponse>>();
var handover = Assert.Single(alerts.Where(a => a.Type == nameof(AlertType.SHIFT_HANDOVER)));
Assert.Contains("1 of 1", handover.Body);
```

A test that asserts `_eventBus.PublishAsync` was called would still pass if no module were
listening. Also cover idempotency — publish or sweep twice and assert one alert — because the bus
may redeliver.

## Coverage thresholds

Enforced by `tools/StoreOps.CoverageGate` as a hard gate, not a guideline:

| Scope | Minimum line coverage |
| --- | --- |
| Service layer (`*Service`) | 80% |
| Route layer (`*Controller`) | 70% |
| Shared utilities (`StoreOps.Api.Shared.*`) | 60% |
| Overall project | 70% |

```bash
dotnet test StoreOps.sln /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
dotnet run --project tools/StoreOps.CoverageGate -- \
  --report tests/StoreOps.Api.Tests/TestResults/coverage.cobertura.xml
```

Coverage is a floor, never the goal. The gate cannot tell a meaningful assertion from
`Assert.NotNull`, which is why the Evaluator scores test quality separately: an AC whose test
executes the code but asserts nothing about the rule is a failed check even at 100% coverage.
