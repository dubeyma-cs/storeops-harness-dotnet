# Architecture journal

Decisions, trade-offs and things learned while building the StoreOps harness, in the order they
came up. Kept as a running log rather than a tidy retrospective — the reasoning at the time is the
useful part, including where it turned out to be wrong.

---

## J-1 — `Task` cannot be called `Task`

The capstone specification names the activities module's entity `Task`, with `TaskStatus`,
`TaskPriority` and `TaskCategory`. In an async C# codebase `Task` collides with
`System.Threading.Tasks.Task` on every line that returns one.

Options: alias `using DomainTask = …` (poisons every file), fully qualify
`System.Threading.Tasks.Task` (worse), or rename the entity.

Renamed to `Activity`. The module is already `activities` and the route is already
`/api/activities`, so the entity name was the outlier. Same reasoning applied to
`Project → Programme` (matches `/api/programmes`) and `User → StaffMember` ("user" means nothing on
a shop floor). Enum **members** are kept verbatim — `IN_PROGRESS`, `SLA_BREACH`, `STORE_SUMMARY` —
because those are on the wire and the wire contract outranks .NET naming convention.

The mapping is recorded in `app-context` as a table rather than left as tribal knowledge, because
every agent needs it and a naming disagreement between the spec and the code would show up as a
phantom finding in review.

**Also noticed:** the specification's own Step 2 says to verify `GET /api/tasks`, while its
endpoint inventory in § 3.6 says `/api/activities`. Treated § 3.6 as authoritative — it is the
detailed contract, and it agrees with the module names.

## J-2 — Where the authentication middleware lives

`StaffAuthenticationMiddleware` needs `IStaffService`. The obvious home is `Shared/Auth/`,
alongside `StaffContext`.

That would have been the first `SO-006` violation, written by the architect. `Shared` importing a
module makes the kernel a participant in a five-way cycle. Moved it into `Modules/Staff/`, which
turns out to be the more honest placement anyway: the specification says the staff module owns
"registration, authentication, and profile management".

`Shared/Auth/` keeps only `StaffContext` and `IStaffContextAccessor` — the *shape* of an identity,
with no knowledge of how one is obtained. Swapping the seeded roster for Entra ID changes one file
inside one module.

## J-3 — Event payloads carry strings, not enums

First draft of `ActivityStatusChangedEvent` carried `ActivityPriority` and `ActivityCategory`
directly. It compiled and read nicely.

It also meant the `alerts` module had to `using StoreOps.Api.Modules.Activities;` to read an
event — reintroducing exactly the coupling the bus exists to remove. The bus would have been
decoration over a dependency.

Payloads now carry enum values as strings, and every event contract lives in
`Shared/Events/Contracts.cs` rather than in the publishing module. `alerts` reacts to `"CRITICAL"`
without importing anything from `activities`. The cost is losing compile-time exhaustiveness on the
subscriber side; the benefit is that the module graph stays honest, and `ArchCheck` can verify it.

## J-4 — Per-item failures are data, not exceptions

The `SO-003` error contract says services throw `AppError` subclasses. The handover endpoint has to
report per-item failures *and* commit the items that succeeded.

Throwing on the first bad item makes partial success impossible, so the two requirements looked
contradictory. Resolved by separating the error **vocabulary** from the error **transport**: a
whole-request failure (duplicate ids, over the cap) throws `ValidationError`; a per-item failure
becomes a `BulkStatusItemResult` carrying the same `ErrorCodes` constant the envelope would have
used.

So a client sees `RESOURCE_NOT_FOUND` whether it arrives in `error.code` or in
`results[2].errorCode`. Written into `spec.md` under *Architectural impact* so it reads as a
decision rather than an inconsistency — this is the kind of thing that, undocumented, gets
"corrected" by the next person.

## J-5 — The status-code `switch` belongs in the controller, and only just

```csharp
var statusCode = (result.Updated, result.Failed) switch
{
    (_, 0) => StatusCodes.Status200OK,
    (0, _) => StatusCodes.Status409Conflict,
    _      => StatusCodes.Status207MultiStatus,
};
```

Is mapping counts to 200/207/409 a business rule (service) or HTTP shaping (controller)?

Controller, because it is purely an HTTP concern: a non-HTTP caller of
`BulkUpdateStatusAsync` has no use for it. The line that makes it safe is that the `switch` reads
only two integers. The moment it inspects an `Activity`, it is a business rule in the wrong layer —
which is precisely what the Generator did in sprint 1 iteration 1, and what `R-1` caught.

`R-1`'s recipe was written *after* seeing that failure, and it is more specific than it would have
been otherwise: an action body fails if it contains a comparison against a domain enum, a loop over
domain entities, more than one service call, or a conditional inspecting an entity field.

## J-6 — StyleCop 1.1 predates most of the C# in this codebase

Enabling `StyleCop.Analyzers` with `TreatWarningsAsErrors` produced 28 errors on the first build,
and 27 of them were the analyser being older than the language:

- `SA1206` demands `required public` — but `public required` is the only order the compiler accepts
- `SA1008`/`SA1009` fire on `is not (A or B)` patterns and `var (a, b)` deconstruction
- `SA0001` fires because `GenerateDocumentationFile` is off (which it is so `CS1591` does not
  demand XML docs on every internal type)

Suppressed those, with a one-line reason each in `.editorconfig`, and kept the rules that catch
real problems. The alternative — a prerelease StyleCop — trades known limitations for unknown ones
in a build that is a hard gate.

The important consequence is procedural, not technical: because `.editorconfig` is where "0
warnings" is *defined*, editing it is how a Generator could pass any gate it liked. Hence
`generator.agent.md` rule 6 and review recipe `R-8` — changing a gate's configuration is an
automatic FAIL, and it escalates rather than looping, because another iteration would just repeat
an instruction that has already been ignored.

## J-7 — `depcruiser` has no .NET equivalent, so the gate got built

The capstone names a dependency analyser for the Node stacks. The .NET options were:

| Option | Why not |
| --- | --- |
| Roslyn analyser in `src/` | reports per-file; cannot answer "is there an `activities → alerts` edge in the graph?" |
| `NetArchTest` | works on compiled assemblies; StoreOps is one assembly with modules as folders, so most rules have nothing to assert on |
| LLM-only review | non-reproducible, which defeats the purpose |

Wrote `tools/StoreOps.ArchCheck`: derives type ownership from declarations, enforces `SO-001`…
`SO-007`, exits non-zero, prints the module graph, and emits markdown for the review archive.

Two implementation notes that were not obvious up front:

1. **Comments must be stripped before scanning.** `IStaffRepository.cs` has a doc comment saying
   "other modules reach staff through `IStaffService`" — which a naive scan reports as a layering
   violation. A gate that blocks a run cannot afford false positives, so `SourceFile.StripComments`
   blanks comment content while preserving line numbers.
2. **The gate needs negative tests.** A scanner that silently matches nothing looks like a clean
   pass forever. `ArchitectureRuleTests` feeds it synthetic trees breaking one rule each, plus the
   comment case above, and asserts the right code fires.

Deriving ownership from declarations rather than an allowlist is what makes it maintainable: new
modules, services and repositories are covered without touching the tool.

## J-8 — Coverage needed its own tool for one specific reason

`dotnet test /p:Threshold=70` enforces a single global number. The StoreOps standard sets four
floors: service 80%, routes 70%, shared 60%, overall 70%.

A single overall threshold passes comfortably while the service layer — where every business rule
lives — drifts to 50%, because controllers and DTOs are cheap to cover and pull the average up.
That is the drift the standards team asked the harness to prevent, so the gate has to be
layer-aware. Hence `tools/StoreOps.CoverageGate`.

Two deliberate details:

- Coverage is computed from `<line hits="n"/>` elements, not from cobertura's `line-rate`
  attributes. The attributes are pre-rounded per class, so summing them gives a different answer
  than counting lines — and at the boundary where 79.6% and 80.0% decide a verdict, "different
  answer" means "non-deterministic gate".
- A malformed or missing report exits **2**, not 1. A broken coverage report is not low coverage;
  the Evaluator records `INDETERMINATE` and escalates rather than blaming the Generator for a
  build-environment problem.

## J-9 — Test isolation is a design decision, not a detail

Repositories are singletons, so a shared `IClassFixture` shares state across a class's tests. That
is fine when every assertion is scoped to an id the test created.

It broke — quietly — for the SLA tests. The sweep is store-wide, so an unresolved breach left by
one test is picked up by the next test's sweep, and `Assert.Equal(1, escalated)` starts depending
on execution order. xUnit gives no ordering guarantee, so the failure would have been
intermittent: the worst possible failure mode inside a harness whose gates must be deterministic.

Rule now in `how-to-test`: **share a fixture for per-id assertions, build a host per test for
store-wide ones.** `SlaAlertFlowTests`, `ReportEventFlowTests` and `HandoverNotificationTests` each
construct their own `StoreOpsApiFactory`.

## J-10 — One approval gate, and where it paid for itself

The loop pauses once, at the spec, and never again. Two sprints, one interruption.

The artefact that justifies it is the Planner's *Design decisions carried by this contract* table:
`207` for partial success, a 100-item cap, duplicate ids rejected wholesale, `BLOCKED` requires a
reason. Each is a business decision with a proposed value and one line of reasoning — a two-minute
read and a one-word reply.

The Intent genuinely did not say whether a mixed outcome is a success. No agent can settle that
defensibly, and guessing wrong means the loop converges confidently on the wrong contract with
every gate green. That specific risk is what the gate buys, for about 6 minutes of a 55-minute run.

## J-11 — The Evaluator mis-scored its own framework

Sprint 1 iteration 1 initially reported 79 by scoring the AC-coverage check pro-rata: ten of twelve
criteria had tests, so 0.83 of a check. `evaluation-criteria` defines checks as binary per
dimension. Corrected to 65.

The verdict was unaffected — a hard gate had already failed, and both numbers are below 80 — but
the recorded *reason* would have been wrong, and a run log is only as useful as its accuracy.

Two things came out of it. `evaluation-criteria` now states "Each check is binary" in the
scored-checks preamble and shows the dimension arithmetic explicitly. And the Monitor logged it as
a drift signal against the framework document itself, which is the uncomfortable but correct
conclusion: the non-determinism strategy is an artefact that can drift like any other, and it
probably needs its own tests rather than only its own prose.

## J-12 — What the first run really produced

Four skill-file drift signals from sprint 1; one from sprint 2. Iterations fell from 2 to 1 on a
sprint with a harder rule.

The honest reading is that the first run against any codebase is partly a skill-file calibration
exercise. The skill files were written against an imagined Generator; sprint 1 was the first time
they met a real one. Every signal was a real gap — an audit trail is a side effect; here is the
correct controller shape; do not refactor outside scope; checks are binary — and each became one
specific edit with its evidence recorded in the run log.

The most useful single data point in the whole run: after adding satisfying *and* violating code
shapes side by side to `architecture-principles`, sprint 2 satisfied `SO-002` — the rule the
client's standards team blocked the rollout over — on the first attempt, with the module graph
unchanged. A rule stated abstractly gets violated. A rule shown as two code blocks gets followed.
