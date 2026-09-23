# Skill: how-to-review

**Read by:** Evaluator · **Budget:** 3 pages

## Purpose

The review procedure, and — the important part — the **binary conversion recipe** for every check
that is a judgement rather than an exit code. `evaluation-criteria` says what the checks are and
what they are worth; this file says how to decide one without the answer depending on the day.

## Evidence rules

1. **Write the evidence before the result.** For every check: file, line, and the code or command
   output that decides it. If you cannot produce an evidence line, the result is `INDETERMINATE`.
2. **Evidence is quoted, not summarised.** "`ActivityService.cs:41` — `private readonly
   IAlertService _alerts;`" is evidence. "the service seems coupled to alerts" is not.
3. **Never re-derive a tool's answer.** `StoreOps.ArchCheck` exit 0 means `SO-001`…`SO-007` pass.
   Reading the code and overriding the tool reintroduces the variability the tool removes. If you
   believe the tool is wrong: `INDETERMINATE` plus a note, which escalates to a human.
4. **Read only the changed files** named in the Generator's summary, plus the contract, the summary
   and the four tool outputs. Reviewing the whole tree makes the cost of iteration 3 unbounded and
   invites findings about code this sprint never touched.
5. **Findings are about code, never about the Generator.**

## Procedure

**Step 1 — automated gates.** Run the four commands from `evaluator.agent.md` and record exit
codes verbatim. Exit 0 pass, non-zero fail, cannot-run `INDETERMINATE`.

**Step 2 — AC coverage.** For each contract AC, find its row in the self-check table, and resolve
the `Verifying test` name against `dotnet test StoreOps.sln --list-tests`. Missing row,
unresolvable name, or duplicate AC → fail, naming the AC id.

**Step 3 — judgement checks.** Work the checklist in order, applying the recipes below. One check
at a time; do not form an overall impression first and fit checks to it.

**Step 4 — arithmetic.** Apply `evaluation-criteria` exactly. Hard gate failed → `FAIL` whatever
the score. Hard-gate `INDETERMINATE` → `ESCALATE`.

---

## Conversion recipes

Each recipe defines what counts as present and what counts as absent. Apply the recipe literally.
There is no third outcome except `INDETERMINATE`, and `INDETERMINATE` means the recipe itself did
not reach a verdict — not that the code is borderline.

### R-1 — Business logic in a route (`SO-004`)

Read every changed `*Controller.cs` action body.

- **Fail** if an action body contains any of: a comparison against a domain enum value; a `foreach`
  or LINQ over domain entities; a call to more than one service method; a conditional that inspects
  an entity field.
- **Pass** if every action body is: bind → single service call → status-code mapping, where the
  mapping uses only counts, `null` checks, or values already returned by the service.
- `INDETERMINATE` if the action calls a helper defined in the controller that you cannot classify.

Reference pass: `ActivitiesController.BulkUpdateStatusAsync` — the `switch` reads
`result.Updated` and `result.Failed` only, and never an `Activity`.

### R-2 — Cross-module side effect routed through the bus (`SO-002`)

List every behaviour the contract describes as affecting another module.

- **Pass** if for each one: a contract record exists in `Shared/Events/Contracts.cs`, the owning
  service calls `PublishAsync` with it, and a `*EventSubscriptions.cs` in the consuming module has
  an `On<TEvent>` line for it.
- **Fail** if the effect is achieved by a direct call, or if a `PublishAsync` exists with no
  subscriber for that type (a published event nobody handles is a silent no-op).
- `INDETERMINATE` if the contract's wording does not make clear whether the effect crosses a
  module boundary — and say which AC is ambiguous.

### R-3 — Test asserts the business rule, not just the status code

For each AC's verifying test, count assertions by category: status code, response body, persisted
state re-read, side effect (audit rows or a recipient's alert list).

- **Pass** for a write AC if the test asserts the status code **and at least one** of persisted
  state or side effect.
- **Pass** for a read AC if the test asserts the status code and at least one field of the body
  that the AC's THEN clause names.
- **Fail** if the only assertions are the status code, or `Assert.NotNull`, or a count with no
  field assertion.
- `INDETERMINATE` if the test is data-driven and you cannot tell which case covers the AC.

### R-4 — Negative path asserted

- **Pass** if, for every AC whose THEN clause is a rejection, the test asserts both the error code
  from `ErrorCodes` **and** that state did not change (re-read, or audit-row count unchanged).
- **Fail** if the rejection is asserted only by status code, or if no test covers the rejection.

### R-5 — Error contract completeness (`SO-003`)

`StoreOps.ArchCheck` already proves no raw throws. This check is the part a scanner cannot see.

- **Pass** if every new `throw new <X>Error(...)` uses a code from `ErrorCodes`, the chosen
  `statusCode` matches the table in `api-integration`, and no controller constructs an error body.
- **Fail** if a new error code string is introduced inline instead of as an `ErrorCodes` constant,
  or if the status code contradicts the table (e.g. a not-found returning `400`).

### R-6 — Store scoping preserved

- **Pass** if every new repository read or write takes a store id, and every new service method
  derives it from `Caller.StoreId` (or an explicit store id parameter on a cross-module read).
- **Fail** if a new repository method looks up by id alone and is called from a request path.
- `INDETERMINATE` for background paths where you cannot tell which identity is in play — name the
  file.

### R-7 — Read-only reports (`SO-005`)

Tool-covered. This check is only: **Fail** if a changed `reports` file gained a write-shaped method
name (`Create…`, `Update…`, `Delete…`, `Recalculate…`) that mutates another module's data through
any route, including a new event the reports module publishes to make another module write.
Otherwise **pass**.

### R-8 — No gate was weakened

Diff-based and non-negotiable.

- **Fail** if this sprint changed `.editorconfig`, `Directory.Build.props`, a coverage threshold in
  `tools/StoreOps.CoverageGate`, a rule in `tools/StoreOps.ArchCheck`, or added
  `#pragma warning disable` / `[SuppressMessage]` anywhere in `src/`.
- **Pass** if none of those files changed.

A weakened gate is the one finding that should always escalate rather than loop: the Generator
cannot fix it, because the instruction not to do it was already in `generator.agent.md`.

### R-9 — Scope discipline

- **Pass** if every changed file is named in the contract's scope, or is a direct consequence (the
  DTO file for a new endpoint, the module's composition root for a new registration).
- **Fail** if a file in an unrelated module changed, or if a refactor the contract does not mention
  appears in the diff.

### R-10 — Contract quality feedback (scored, does not block)

Reviews the *Planner's* output, not the Generator's — it is how skill-file drift surfaces.

- **Fail** if any AC's THEN clause names no observable outcome, or if two ACs describe the same
  behaviour, or if a collection endpoint's contract is missing one of the mandatory criteria from
  `sprint-decomposition` (cap, duplicates, empty, partial failure, unauthenticated).
- **Pass** otherwise.

A `Fail` here is reported to the Monitor as a drift signal against `sprint-decomposition`, and does
not count against the Generator.

---

## Writing findings

```markdown
### F-2 — Handover status set is duplicated in the controller  [R-1, SO-004]
**File** `src/StoreOps.Api/Modules/Activities/ActivitiesController.cs:78`
**What** The action filters `request.Items` to `DONE`/`BLOCKED` before calling the service.
**Why it fails the rule** Which statuses a handover may set is a business rule. It already lives in
`ActivityStatusPolicy.HandoverStatuses`; a second copy in the route will drift from it, and the
route's copy is unreachable from the service, so a non-HTTP caller bypasses it.
**Required change** Delete the filter. The service already rejects non-handover statuses per item
via `ValidateHandoverItem`.
```

One finding per defect. File and line always. `Required change` is one concrete instruction, not a
menu — the Generator has one iteration, and an ordered list of unambiguous changes is the only
input that reliably fits in it.

## Feedback ordering

Order the *Feedback for the Generator* list by blocking power:

1. failed hard gates, automated first (build, tests, arch, coverage)
2. failed hard gates, judgement
3. failed scored checks, heaviest dimension first
4. `INDETERMINATE` checks, each with the specific missing information

Never include a suggestion that is not tied to a failed check. "While you are there, consider…" is
how a three-iteration budget becomes a two-iteration budget.
