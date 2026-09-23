# Harness Design Brief — StoreOps Development Harness

**Track:** Build (Solution Architects / Senior Architects) · **Application:** StoreOps API
(ASP.NET Core, .NET 8) · **Harness:** `CLAUDE.md` + four agents + eight skill files + two
deterministic gate tools

This brief documents the architectural intent behind the working harness in this repository: how
one business Intent is decomposed into agent responsibilities, how the governance layer enforces
StoreOps standards, how variable LLM output becomes a reproducible verdict, and the decisions that
shaped all three.

---

## Section A — Intent Decomposition

### From Intent to agent responsibilities

The harness has four agents, and the division between them is drawn along one axis: **who is
allowed to be wrong about what**.

| Agent | Owns | Deliberately cannot |
| --- | --- | --- |
| Planner | what "done" means | see the implementation — it reads no source file except the endpoint inventory |
| Generator | how it is built | grade itself, or change the contract it was given |
| Evaluator | whether it is done | change code, or re-invoke the Generator |
| Monitor | what it cost and what is drifting | judge code, or propose a code change |

That separation is the reason a verdict means anything. An agent that both writes code and decides
whether the code is acceptable will always find it acceptable; the handoff file between Generator
and Evaluator exists so the second judgement is made from evidence rather than from memory of
having written it.

The Monitor's exclusion is equally deliberate. It is the only agent that reads across sprints, so
it is the only one positioned to notice that the same rule keeps being violated — but if it could
also propose code fixes, its log would become an opinion column rather than a record.

### Sprint boundaries for the demonstration feature

The Intent (verbatim in [`PROMPT.md`](PROMPT.md)) asked for four things: batch status changes,
partial-failure tolerance, an audit record per change, and notifying store management. The Planner
split it into two sprints:

| Sprint | Scope | ACs |
| --- | --- | --- |
| 1 | `PATCH /api/activities/bulk-status` — request contract, per-item validation, partial-failure resolution, status-code selection, one audit entry per updated activity | AC-1 … AC-12 |
| 2 | publish `ActivitiesBulkStatusAppliedEvent`; `alerts` raises a `SHIFT_HANDOVER` notification to store management | AC-13 … AC-15 |

**Why the boundary is there.** The cut is between a synchronous request contract and an
asynchronous cross-module side effect, which is heuristic 1 in
[`sprint-decomposition`](.harness/skills/sprint-decomposition/SKILL.md). Three reasons make it the
right cut rather than a tidy-looking one:

1. **Different test shapes.** Sprint 1's assertions read a response body and re-read a resource.
   Sprint 2's read *a different person's alert list* after an event. Mixed into one sprint, a
   passing response-body assertion masks a notification that was never delivered — and "missing
   event bus integration" is failure mode 4 from the client context, so hiding it is the specific
   thing this harness exists to prevent.
2. **Different modules' write surfaces.** Sprint 1 writes only in `activities`; sprint 2 writes
   only in `alerts`. A sprint writing in two modules is either violating `SO-002` or is two
   sprints.
3. **Each sprint ends green.** Sprint 1 is a complete, shippable, fully tested endpoint with no
   reference to `alerts`. If the run had stopped after sprint 1, the branch would have been
   mergeable.

What is deliberately *not* a boundary: the DTOs, the service method and the controller action all
land in sprint 1 together. They are one behaviour expressed at three layers, and separating them
would leave sprint 1 unable to compile, let alone end green.

### What made the acceptance criteria testable

The criterion grammar is fixed — `GIVEN <state> WHEN <action> THEN <observable outcome>` — but the
grammar is not what makes a criterion testable. The rule that does the work is the constraint on
the THEN clause: **it must name at least one of** a status code *plus* a state change, a stored
record with the field that proves it, a published event by contract type, or a rejected transition
with its error code.

A status code alone is explicitly not an observable outcome. That single restriction is failure
mode 3 from the client context written into the contract template, before any code exists.

The concrete difference:

| Rejected by the grammar | Accepted |
| --- | --- |
| THEN an audit record is created | THEN exactly one `ActivityAuditEntry` exists for that activity with `source` `api.bulk-status`, `action` `STATUS_CHANGED`, `fromStatus` `TODO`, `toStatus` `BLOCKED`, that reason, and `actorStaffId` equal to the calling staff member |
| THEN the alerts module is notified | THEN every `STORE_MANAGER` in that store has a `SHIFT_HANDOVER` alert whose body names the acting staff member and reports "1 of 1" updated |

The second rejected form matters more than it looks: *"the alerts module is notified"* describes a
**call**, and a Generator implementing a call reaches for `IAlertService` — an `SO-002` violation
invited by the contract itself. Rewriting it as an outcome on a recipient's list makes the event
bus the only way to satisfy it.

The contract template also mandates five criteria for any collection endpoint — maximum size,
duplicates, empty collection, partial-failure survivors, unauthenticated — because the first draft
of this feature had none of them.

### One example sprint contract entry, in full

From [`.harness/reviews/sprint-1-contract.md`](.harness/reviews/sprint-1-contract.md):

> ### AC-2
> GIVEN one `TODO` activity, one `DONE` activity, and one id that does not exist
> WHEN the caller submits a handover batch setting all three to `DONE`
> THEN the response is `207`, `updated` is 1, `failed` is 2, the `DONE` activity's item carries
> `errorCode` `INVALID_STATE_TRANSITION`, the unknown id's item carries `RESOURCE_NOT_FOUND`, and
> the `TODO` activity reads back as `DONE`
> Layer: service

Everything a test needs is in the THEN clause, and nothing is left to interpretation: the status
code, two counts, two specific error codes, and — the clause that carries the actual business
value — *the `TODO` activity reads back as `DONE`*. That last one is what makes it a partial-failure
test rather than a multi-status test. Its verifying test is named in the Generator's self-check
table, and the Evaluator's `H2.2` gate resolves that name against `dotnet test --list-tests`.

---

## Section B — Governance Framework

### Skill file strategy

Eight skill files, in three tiers. The tiering is a token-budget decision as much as a
comprehension one: every agent pays for tier 1 on every invocation.

| Tier | File | Read by | Budget | Encodes |
| --- | --- | --- | --- | --- |
| Shared | `app-context` | all four | 1.5 pp | modules, layers, endpoint inventory, the `Task→Activity` naming map, the seeded roster |
| Shared | `architecture-principles` | Planner, Generator, Evaluator | 3 pp | `SO-001`…`SO-007`, each with satisfying *and* violating C# |
| Planner | `sprint-decomposition` | Planner | 2 pp | boundary heuristics, THEN-clause constraint, contract template |
| Generator | `coding-conventions` | Generator | 3 pp | C# 12 conventions, the StyleCop ruleset and why each suppression exists |
| Generator | `api-integration` | Generator | 2.5 pp | route/DTO conventions, status-code table, error envelope, event publication, batch checklist |
| Generator | `how-to-test` | Generator | 2.5 pp | assertion requirements, fixed clock, fixture isolation, coverage floors |
| Evaluator | `how-to-review` | Evaluator | 3 pp | review procedure and the ten binary conversion recipes |
| Evaluator | `evaluation-criteria` | Evaluator | 3 pp | dimensions, weights, hard gates, verdict arithmetic |

**Which are shared, and why.** Only two. `app-context` is shared because an artefact that calls the
same thing by two names is not an audit trail — when the run log says `SO-004` and the feedback
says "layering", a reader cannot join them. `architecture-principles` is shared because the Planner
must not draw a sprint boundary that forces a violation, the Generator must satisfy the rules, and
the Evaluator must judge them; three private copies would drift.

Everything else is private to one agent, which is what keeps the Evaluator's context at ~16k
instead of ~30k. Adding a file to an agent's read list is a harness change, reviewed like code.

**What is StoreOps-specific rather than generic.** Each file is anchored in this codebase, and the
anchors are the reason a generic principles document would not have worked:

- `architecture-principles` names `IStaffService` as the sanctioned cross-module read and
  `IStaffRepository` as the violation, in adjacent code blocks. It explains why event contracts
  live in `Shared/Events/Contracts.cs` and why payloads carry enum values as strings.
- `api-integration` gives the exact 200/207/409 count-based `switch` for the batch endpoint, and
  states that an id in another store returns `404` not `403` — a StoreOps information-disclosure
  decision, not a REST convention.
- `how-to-test` names `StoreOpsApiFactory`, the two things it substitutes, and the rule for when a
  test needs its own host (store-wide assertions) versus a shared fixture (per-id assertions).
- `coding-conventions` records that enum members stay `SCREAMING_SNAKE_CASE` because they are on
  the wire, deliberately breaking .NET convention.

### `.harness/reviews/` as a governance audit trail

**What is captured.** Per sprint: the contract, the Generator's self-check summary, the Evaluator's
full per-check report including every failed iteration, the Monitor's run log, and the raw output
of both gate tools. Once per run: the approved spec with its `STATUS: APPROVED <timestamp>` marker.

The chain is `contract → generator-summary → evaluator-feedback → run-log`: what was agreed, what
was built (self-reported), what was verified, what it cost. A claim in the summary that the
feedback does not confirm is the most interesting thing in the archive — and in this run there was
one: sprint 1 iteration 1 reported the audit trail as implemented, and the Evaluator found no test
asserting it.

**Who can access it.** It is in the application repository, so read access follows repository
access: the eight-member squad, the client's standards team representative, and every pull-request
reviewer. Write access is the Monitor's during a run. Nobody edits an archived artefact — its
entire value is that it is what the agents actually wrote.

The split from `.harness/output/` (gitignored, overwritten per iteration) is what makes this
usable: working files churn once per iteration and would bury the history, while archiving on
verdict preserves the record of *why* a sprint needed three attempts.

**How it surfaces a recurring quality issue.** Two greps over the archive:

```bash
grep -ho "SO-00[0-9]" .harness/reviews/*-evaluator-feedback.md | sort | uniq -c | sort -rn
grep -h "Iterations used" .harness/reviews/*-run-log.md
```

A rule code in two or more consecutive sprints means the skill file states the rule but not the
code shape that satisfies it — the fix is a skill-file edit, not another Generator iteration.
Rising iteration counts mean the skill files are lagging behind the codebase.

This is not hypothetical. Sprint 1 produced four drift signals; each became a specific skill-file
edit, logged with its evidence in
[`sprint-1-run-log.md`](.harness/reviews/sprint-1-run-log.md). Sprint 2 produced one, and satisfied
`SO-002` on the first attempt. The trend table in the run log is the mechanism that makes that
visible rather than anecdotal.

### One skill file rule, and what breaks without it

From [`architecture-principles`](.harness/skills/architecture-principles/SKILL.md), rule
**`SO-002` — Event bus only**:

> A side effect that crosses a module boundary must be raised through `IEventBus`. A module must
> not depend on a downstream side-effect module's service. `alerts` and `reports` are side-effect
> modules: nothing may call into them directly.
>
> Satisfying shape:
> ```csharp
> await _eventBus.PublishAsync(
>     new ActivitiesBulkStatusAppliedEvent(storeId, actorStaffId, requested, updated, failed, ids),
>     cancellationToken);
> ```
> Violating shape:
> ```csharp
> private readonly IAlertService _alerts;            // SO-002
> await _alerts.NotifyHandoverAsync(storeId, count);  // SO-002
> ```

**What breaks without it.** Three things, in increasing order of cost.

1. *`activities` can no longer be tested or deployed without `alerts`.* The dependency is
   compile-time, so the two modules become one deployment unit — and the squad's eight developers
   lose the ability to work on them independently, which was the point of modularising StoreOps.
2. *A store colleague's handover fails because of an alert.* `InMemoryEventBus` logs and swallows
   subscriber failures precisely so a broken alert handler cannot fail the request that published
   the event. A direct call has no such isolation: an exception in `AlertService` propagates into
   the handover response, and forty successful status changes are reported as a `500`.
3. *The dependency graph becomes a cycle the moment `alerts` needs to read an activity.* It already
   does — the escalation sweep calls `IActivityService.FindForStoreAsync`. With a direct
   `activities → alerts` edge added, `SO-007` fails and the modules can no longer be reasoned about
   separately at all.

The rule is enforced structurally as well as textually, which is the part that makes it hold up
under a Generator that is trying to be helpful:

- `StoreOps.ArchCheck` derives which modules own which service types from where they are declared,
  and fails the build on any reference from a non-side-effect module to `IAlertService` or
  `IReportService`.
- `IAlertService` has **no** `CreateNotificationAsync(recipient, text)`. Every member that creates
  an alert takes a `DomainEvent`. There is no signature for another module to call even if it
  obtained the interface.
- `ArchitectureRuleTests` asserts the module graph directly, so AC-15 verifies the rule rather than
  assuming it.

---

## Section C — Non-Determinism Strategy

### Dimensions and weights

| # | Dimension | Weight | Why this dimension for StoreOps |
| --- | --- | --- | --- |
| D1 | Architecture conformance | **35%** | the client's standards team blocked the Claude Code rollout over boundary and event-bus violations. This is the dimension that unblocks it, and it is the most mechanically checkable, so it carries the most weight |
| D2 | Contract fulfilment | **30%** | the harness's proposition is that an approved contract is what ships. An unmet AC is worse than a style problem |
| D3 | Test quality and coverage | **25%** | failure mode 3 was tests asserting status codes and nothing else. Coverage cannot detect that, so it is scored as well as gated |
| D4 | Error contract and API consistency | **10%** | narrow and largely tool-enforced, so it is weighted to break ties rather than decide verdicts |
| | **Total** | **100%** | |

The weighting is a direct reading of the client context: three of the four failure modes are
architectural, so D1 gets more than a third of the weight, and a single D1 failure (7 points) is
survivable while three (21 points) are not.

### Hard gates, and why none of them can be soft

Every dimension has at least one **automated** gate — an exit code or a parsed report, never a
judgement.

| Gate | Dim | Type | Prevents | Why it cannot be a soft check |
| --- | --- | --- | --- | --- |
| H1.1 `dotnet build --warnaserror` | D1 | automated | nullable and analyser regressions | the repository's definition of "compiles" includes zero warnings; a partial build is not a starting point for any other measurement |
| H1.2 `StoreOps.ArchCheck` exit 0 | D1 | automated | all four client failure modes, structurally | a weighted score would let a cross-module repository import through on the strength of good tests — exactly the merge the standards team refuses |
| H1.3 no gate weakened | D1 | judgement (R-8) | disabling a rule to pass it | a weakened gate invalidates every other result in the report, including the passes. It cannot be traded against anything |
| H2.1 `dotnet test` exit 0 | D2 | automated | shipping a red suite | if tests fail, no other measurement is trustworthy |
| H2.2 AC coverage resolves | D2 | automated | unverifiable "MET" claims | without it the self-check table is an assertion, and the whole contract mechanism is decorative |
| H3.1 `StoreOps.CoverageGate` exit 0 | D3 | automated | per-layer coverage drift | a numeric floor is the one objective part of test adequacy; soft, it drifts down one sprint at a time |
| H3.2 rules, not just status codes | D3 | judgement (R-3) | failure mode 3 | coverage cannot see it — a test can execute every line and assert nothing |
| H4.1 `ErrorContractTests` all pass | D4 | automated | a second error shape on the wire | one envelope is a client-facing contract; a partial regression is invisible until a client breaks |

Note `H3.1`'s existence at all: `dotnet test /p:Threshold=70` enforces one global number, and the
StoreOps standard sets four floors by layer. A single overall threshold passes happily while the
service layer — where the business rules are — drifts to 50%. That is why the gate is a tool rather
than an MSBuild property.

### How the same inputs produce the same verdict

```
score = Σ over dimensions ( checks_passed / checks_in_dimension × weight )   [half-up rounding]

VERDICT = ESCALATE  if any hard-gate check is INDETERMINATE
        = FAIL      if any hard gate FAILED
        = FAIL      if score < 80
        = PASS      otherwise
```

Four mechanisms make that reproducible rather than merely written down:

1. **Every check is binary.** No 1–5 scale, no partial credit. `evaluation-criteria` states it
   explicitly because the Evaluator got it wrong once — see the walkthrough below.
2. **Every judgement check has a named conversion recipe.** `how-to-review` supplies ten (`R-1`…
   `R-10`), each defining what counts as present and what counts as absent. `R-1` does not ask "is
   there business logic in this controller?" — it says an action body fails if it contains a
   comparison against a domain enum, a loop over domain entities, more than one service call, or a
   conditional inspecting an entity field.
3. **Evidence precedes result.** No check may be recorded without a file, line, and the code or
   command output that decides it. A check with no evidence line is `INDETERMINATE`, never a pass.
4. **Tool answers are never re-derived.** If `ArchCheck` exits 0, `SO-001`…`SO-007` pass. Reading
   the code and overriding the tool would reintroduce the variability the tool removes; believing
   the tool is wrong is an `INDETERMINATE` plus a note, which escalates to a human.

### Walkthrough — variable Generator output to a definitive verdict

Sprint 1, iteration 1. The Generator produced a working endpoint, and also did two things nobody
asked for: it filtered the handover status set inside the controller, and it reformatted an
unrelated file in the `programmes` module. It omitted tests for the audit trail.

**Step 1, automated gates.** Build exit 0. `ArchCheck` exit 0 — note that the controller filter is
*not* an `SO-004` tool violation, because the tool checks structure (does a controller reference a
repository?) and this was a business rule in the right structural place. Tests exit 0. Coverage gate
exit 0.

**Step 2, AC coverage (`H2.2`, automated).** The contract has AC-1…AC-12. The self-check table has
ten rows. AC-4 and AC-5 are absent, and no test in the suite asserts the audit trail. → **FAIL**,
naming both AC ids. A hard gate has failed, so the verdict is already determined; the Evaluator
still completes the checklist, because the Generator needs the full feedback to fix everything in
one iteration.

**Step 3, judgement checks.** `R-1` applied to `ActivitiesController.cs:78` finds a comparison
against `ActivityStatus` in an action body → `C1.3` **FAIL**. `R-9` finds `ProgrammeService.cs` in
the diff and not in the contract's scope → `C1.5` **FAIL**. `C2.1`, `C2.2` and `C3.2` fail as
consequences of the missing tests.

**Step 4, arithmetic.**

```
D1 = 3/5 × 35 = 21.00
D2 = 2/4 × 30 = 15.00
D3 = 3/4 × 25 = 18.75
D4 = 3/3 × 10 = 10.00
score = 64.75 → 65

H2.2 FAILED  →  VERDICT: FAIL   (score is irrelevant once a hard gate fails)
```

`iteration 1 < 3`, so the orchestrator returns the feedback file to the Generator. The feedback is
five ordered instructions with file and line, nothing else — no suggestions, no alternatives.
Iteration 2 made exactly those five changes: 100/100, PASS, no regressions.

**The self-correction worth reporting.** The iteration-1 report initially scored `C2.2` pro-rata —
ten of twelve ACs had tests, so it recorded 0.83 of a check and a total of 79. That is wrong:
`evaluation-criteria` defines checks as binary per dimension, not per AC. Corrected to 65. Both
numbers are below 80, so the verdict was unaffected, but the *reason* it failed would have been
mis-recorded. The Monitor logged it as a drift signal against `evaluation-criteria`, and the file
now states "Each check is binary" in the scored-checks preamble with the arithmetic shown
explicitly. This is the loop working on itself: a non-determinism strategy is only as good as the
document that describes it, and that document is also an artefact that can drift.

**On the 80 threshold.** With these weights a sprint can lose one D1 check (7) plus one D4 check
(3.3) and still pass at 89.7. Losing a second D1 check drops it to 82.7, and a third to 75.7 — a
fail. One mistake in the heaviest dimension is recoverable in review; a pattern of them is not.

### Escalation path

**Triggers.** Three Generator/Evaluator iterations without a PASS (`ITERATION_LIMIT`), or any
hard-gate check recorded as `INDETERMINATE` (`INDETERMINATE_HARD_GATE`). The second escalates
immediately and does **not** consume an iteration: an Evaluator that cannot decide is a harness
defect, and looping the Generator on it spends tokens without producing new information.

**Output.** `.harness/output/escalation.md`, containing in order: the header (sprint, feature,
iterations used, trigger); the **blocking checks only**, each with rule code, file, line and
reasoning; one line per iteration saying what changed and which check moved; a diff summary with
insertion and deletion counts; the recommended human action, phrased as a decision rather than a
task; and the recipients.

**Recipients.** The squad tech lead owns routing. When the blocking check is an `SO-0xx`
architecture rule, the client's standards team representative is copied — they are the party whose
objection created the harness, and an architecture rule that a Generator cannot satisfy in three
attempts is either a real design tension or a rule that needs revising. Both are their call, not
the squad's.

**Expected response.** A single decision, within one working day, recorded as a reply on the
escalation file. The run does not resume automatically: the developer starts a new run whose Intent
references the escalation, which keeps the audit trail honest about the fact that a human changed
the direction.

**A gate weakening always escalates rather than looping.** If `R-8` fails, the Generator cannot fix
it — the instruction not to weaken a gate was already in `generator.agent.md`, so another iteration
would be repeating an instruction that has already been ignored once.

---

## Section D — Architectural Decisions

### D-1 — A deterministic source analyser as the primary architecture gate

**Decision.** Build `tools/StoreOps.ArchCheck`, a 500-line .NET console analyser that derives type
ownership from declarations and enforces `SO-001`…`SO-007` with a non-zero exit code. Make it the
Evaluator's `H1.2` hard gate and forbid the Evaluator from re-deriving its answers.

**Alternatives considered.**

- *LLM-only assessment against `architecture-principles`.* Rejected: the verdict would vary between
  runs on identical input, which is precisely what the rubric and the standards team are asking the
  harness to eliminate.
- *Roslyn analysers shipped in `src/`.* Better fidelity — a real syntax tree instead of regex over
  stripped comments. Rejected for this build: an analyser reports at the file it is analysing,
  while the harness needs a whole-graph question answered ("is `activities → alerts` in the
  dependency graph?"). The right long-term answer is probably both.
- *`NetArchTest` or a similar assertion library.* Rejected: it works on compiled assemblies, and
  StoreOps is one assembly with modules as folders, so most rules would have nothing to assert on.

**Rationale.** Three of the four client failure modes are structural, and structure is exactly what
a text analyser can decide without judgement. Deriving ownership from declarations rather than an
allowlist means the rules keep working for modules, services and repositories that do not exist
yet — no maintenance burden that would decay. It also prints the module graph on every run, which
turned out to be the single most convincing piece of evidence in the demonstration: sprint 2 added
a cross-module notification and the graph did not change.

**Assumption it depends on.** That modules stay folders in one assembly, with the naming
conventions in `coding-conventions` (`I*Repository`, `*Service`, `*Controller`). If StoreOps were
split into projects per module, project references would make `SO-001` a compile error and half
this tool would become unnecessary — which would be a better world, and the tool's `--json` output
is deliberately the only integration point so it can be replaced without touching the agents.

The tool's own trustworthiness is itself tested: `ArchitectureRuleTests` feeds it synthetic trees
that break one rule each and asserts the matching code fires, plus a case with a banned pattern
inside a comment to prove comment-stripping works. Without those negative tests a scanner that
silently matched nothing would look like a clean pass forever.

### D-2 — One human approval gate, at the spec, and none after

**Decision.** The Planner's `spec.md` ends with `STATUS: AWAITING APPROVAL` and the run stops. After
the developer approves, the Generator/Evaluator loop runs to PASS or escalation with no further
human input.

**Alternatives considered.**

- *Approve every sprint contract.* Rejected: with two sprints per feature this doubles the
  interruptions for no new information — the sprint boundaries and their reasoning are already in
  the spec the developer approved.
- *Approve nothing; run fully autonomously from the Intent.* Rejected: the Intent for this feature
  was genuinely ambiguous about whether a mixed outcome is a success, and there is no defensible
  way for an agent to settle that. Guessing wrong means the loop converges confidently on the wrong
  contract, with all gates green.
- *Approve the diff at the end, as a pull-request review.* Rejected as the *harness's* gate: by
  then the cost is already sunk. Pull-request review still happens; it is not what governs the
  loop.

**Rationale.** The expensive mistakes are contract mistakes, and they are cheapest to catch before
any code exists. The Planner's *Design decisions carried by this contract* table is the artefact
the gate exists for: 207 for partial success, 100-item cap, duplicate ids rejected wholesale,
`BLOCKED` requires a reason. Those are business decisions with a proposed value and one line of
reasoning each, which is a two-minute read and a one-word reply.

The demonstration run bears this out economically: one approval covered both sprints and the
Planner was invoked once for the whole feature, so the gate cost ~6 minutes of the 55-minute run.

**Assumption it depends on.** That the Planner reliably surfaces its ambiguities rather than
resolving them silently. The mitigation is structural — `planner.agent.md` rule 4 requires the
decision table, and the Evaluator's `C2.4` check scores contract quality and reports failures to
the Monitor as `sprint-decomposition` drift rather than as a Generator fault. If a sprint ever
passes every gate while implementing the wrong thing, that check is where the post-mortem starts.

### D-3 — `INDETERMINATE` as a first-class result that escalates instead of failing

**Decision.** Allow the Evaluator a third outcome per check. On a scored check it counts as a fail
for arithmetic but is reported separately. On a hard-gate check it forces `ESCALATE` immediately,
without consuming an iteration.

**Alternatives considered.**

- *Force a binary decision on every check.* Rejected: it makes the Evaluator guess, and a guess is
  where non-determinism re-enters a system built to remove it. Two runs would then disagree on the
  same code.
- *Treat uncertainty as a fail and loop.* Rejected: the Generator cannot fix an ambiguity in the
  contract or a recipe the Evaluator could not apply. It would change something, the check would
  stay indeterminate, and three iterations would burn ~80k tokens producing no information.
- *Treat uncertainty as a pass.* Rejected outright — it is how a gate becomes decorative.

**Rationale.** It separates two genuinely different failures: *the code is wrong* (loop) and *the
harness cannot tell* (ask a human). Only the first is worth a Generator iteration. The mechanism
also feeds the Monitor: a check that goes indeterminate twice is evidence that its conversion
recipe in `how-to-review` is under-specified, which is a specific, actionable skill-file edit
rather than a vague sense that reviews are inconsistent.

The same reasoning is why `StoreOps.CoverageGate` exits **2** rather than 1 on a malformed or
missing report. A broken coverage report is not low coverage; conflating them would blame the
Generator for a build-environment problem.

**Assumption it depends on.** That indeterminate results stay rare — a few percent of checks. If
they became common the harness would escalate constantly and be worse than no harness at all. That
is why every judgement check has an explicit recipe, and why the Monitor tracks indeterminate
counts per sprint as a first-class field in the run log. The demonstration run recorded zero across
both sprints, which is the number to watch.

---

## Closing note

A working harness without documented reasoning is a black box, and a black box cannot be
maintained by the next architect or adapted to the next codebase. The three decisions above are the
ones another team would most likely get differently, and each names the assumption that would make
it the wrong choice. The rest of the design — four agents, eight skill files, four dimensions, four
hard-gate commands — follows from a single premise: **a governance rule that is not enforced by an
exit code is a suggestion**, and the client's standards team has already told us that suggestions
are not enough.
