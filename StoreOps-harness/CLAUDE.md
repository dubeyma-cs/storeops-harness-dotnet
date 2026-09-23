# StoreOps Development Harness — Orchestrator

This file is the entry point and the routing brain of the StoreOps harness. Claude Code reads it
first, and it decides what happens next after every agent hands off.

The harness governs the **StoreOps API** (retail store operations, ASP.NET Core / .NET 8). Its job
is to make the four failure modes the client's standards team found impossible to merge:

1. direct imports from another module's repository, bypassing the service boundary and event bus
2. raw `Exception` throws in services, bypassing the typed `AppError` hierarchy
3. tests that assert HTTP status codes without verifying business-rule compliance
4. missing event bus integration — state changes written straight into a sibling module

---

## 1. Entry prompt format

A developer starts a run with exactly this shape:

```
HARNESS RUN
Feature: <one-line feature name>
Intent: <2–5 sentences of business intent, in retail language, no implementation detail>
Constraints: <optional — deadlines, endpoints that must not change, config that must stay default>
Out of scope: <optional — what this run must not touch>
```

The live example used for the demonstration run is committed at [`PROMPT.md`](PROMPT.md).

On receiving it, invoke the **Planner** and nothing else. Do not write code before a spec exists
and has been approved: an unapproved spec is the single biggest source of wasted Generator tokens.

---

## 2. Agents

Each agent is defined by a file in `.harness/agents/`. An agent reads only the skill files listed
in its own definition — never the whole repository — and writes exactly one handoff artefact.

| Agent | Definition | Reads (skills) | Writes |
| --- | --- | --- | --- |
| Planner | [`.harness/agents/planner.agent.md`](.harness/agents/planner.agent.md) | `app-context`, `architecture-principles`, `sprint-decomposition` | `.harness/output/spec.md`, `.harness/output/sprint-N-contract.md` |
| Generator | [`.harness/agents/generator.agent.md`](.harness/agents/generator.agent.md) | `app-context`, `architecture-principles`, `coding-conventions`, `api-integration`, `how-to-test` | code in `src/` and `tests/`, `.harness/output/generator-summary.md` |
| Monitor | [`.harness/agents/monitor.agent.md`](.harness/agents/monitor.agent.md) | `app-context` | `.harness/reviews/sprint-N-run-log.md` |

Invocation is sequential and explicit. Read the agent definition, then act as that agent for the
duration of the step. Never run two agents in one step — the handoff file is the contract between
them, and a merged step produces a Generator that grades its own work.

---

## 3. The loop

```
                     ┌──────────────────────────────────────────────┐
                     │  HARNESS RUN prompt (PROMPT.md)              │
                     └───────────────────┬──────────────────────────┘
                                         ▼
                                    ┌─────────┐
                                    │ Planner │
                                    └────┬────┘
                                         ▼
                        spec.md  +  sprint-N-contract.md
                            STATUS: AWAITING APPROVAL
                                         ▼
                        ╔════════════════════════════════╗
                        ║  HUMAN APPROVAL GATE (only one)║
                        ╚════════════════┬═══════════════╝
                                         ▼
       ┌──────────────────────── for each sprint N ────────────────────────┐
       │                                                                  │
       │   ┌───────────┐   generator-summary.md   ┌───────────┐           │
       │   │ Generator │ ───────────────────────▶ │ Evaluator │           │
       │   └───────────┘                          └─────┬─────┘           │
       │         ▲                                      │                 │
       │         │  evaluator-feedback.md (FAIL)         │                 │
       │         └──────────────────────────────────────┤                 │
       │                                                ▼                 │
       │                                        ┌──────────────┐          │
       │                                        │   Monitor    │          │
       │                                        └──────┬───────┘          │
       │                                  sprint-N-run-log.md             │
       └────────────────────────────────────┬─────────────────────────────┘
                                            ▼
                         PASS → next sprint, or run complete
```

**Approval.** The Planner's `spec.md` ends with `STATUS: AWAITING APPROVAL`. Stop there and ask the
developer. Once they reply with approval, rewrite the marker to
`STATUS: APPROVED <ISO-8601 timestamp>` and do **not** pause again: the Generator/Evaluator loop
runs autonomously from that point. The developer re-enters only at an escalation.

**Routing.** Read the `VERDICT:` line of `.harness/output/evaluator-feedback.md` and route:

| Verdict | Condition | Action |
| --- | --- | --- |
| `PASS` | all hard gates passed **and** weighted score ≥ 80 | run Monitor, archive artefacts, advance to sprint N+1 (or finish) |
| `FAIL` | any hard gate failed, or score < 80, and `iteration < 3` | run Monitor, re-invoke Generator with the feedback file as its primary input, `iteration += 1` |
| `FAIL` | `iteration == 3` | run Monitor, then **escalate** |
| `ESCALATE` | any hard-gate check is `INDETERMINATE` | run Monitor, then **escalate** immediately, regardless of iteration count |

`iteration` starts at 1 and is per sprint. Three Generator attempts maximum. An `INDETERMINATE`
hard gate never consumes iterations — an Evaluator that cannot decide is a harness problem, not a
code problem, and looping the Generator on it burns tokens without new information.

---

## 4. Escalation output

On escalation, write `.harness/output/escalation.md` and stop the run. It must contain, in order:

1. **Header** — sprint id, feature name, iterations used, trigger (`ITERATION_LIMIT` or
   `INDETERMINATE_HARD_GATE`).
2. **Blocking checks only** — the specific check ids that failed or were indeterminate, each with
   its rule code, file, line and the Evaluator's reasoning. Not the full checklist.
3. **What the Generator tried** — one line per iteration: what changed and which check moved.
4. **Diff summary** — files changed with insertion/deletion counts; full diff is in git.
5. **Recommended human action** — the narrowest question a human must answer, phrased as a
   decision, not a task.
6. **Recipients** — squad tech lead (routing owner) and, when the blocking check is an `SO-0xx`
   architecture rule, the client standards team representative.

The run does not resume automatically. The developer answers the question, then starts a new run
whose Intent references the escalation file.

---

## 5. Automated checks and their relationship to CI

The Evaluator's hard gates are the same four commands the pipeline runs, in the same order:

```bash
dotnet build StoreOps.sln --warnaserror
dotnet test StoreOps.sln
dotnet run --project tools/StoreOps.ArchCheck -- --md .harness/output/arch-check.md
dotnet run --project tools/StoreOps.CoverageGate -- --report tests/StoreOps.Api.Tests/TestResults/coverage.cobertura.xml
```

The harness **precedes and feeds into** CI; it does not replace it.

- **Precedes** — the loop runs on the developer's machine before the branch is pushed, so a
  contract violation costs one Generator iteration instead of a red pipeline and a review cycle.
- **Feeds into** — `.harness/reviews/sprint-N-evaluator-feedback.md` and `sprint-N-run-log.md` are
  committed with the code, so the pull-request reviewer reads the verdict that was already reached
  rather than re-deriving it.
- **Does not replace** — CI re-runs all four commands. The harness runs where a developer could
  skip it; the pipeline is the trust boundary. Anything the harness gates, CI gates too, and CI is
  the authority on merge.

Separation of concerns on disk: harness definitions live in `.harness/`, pipeline definitions in
`.github/workflows/`. Neither directory imports from the other; the shared contract is the four
commands above, which exist as runnable projects in `tools/` precisely so both callers can invoke
them identically.

---

## 6. Context and token strategy

Each agent invocation starts from a **fresh context**. Nothing is carried in conversation state;
everything crosses the boundary as a file. That is what makes the loop resumable and what keeps
iteration 3's context the same size as iteration 1's.

| Agent | Reads | Approx. context |
| --- | --- | --- |
| Planner | 3 skill files + `PROMPT.md` | ~6k tokens |
| Generator | 5 skill files + contract + (on FAIL) feedback + the files it will touch | ~14k tokens |
| Evaluator | 3 skill files + contract + generator summary + changed files + 4 tool outputs | ~16k tokens |
| Monitor | 1 skill file + feedback + summary | ~4k tokens |

Rules that keep those numbers honest:

- An agent reads the skill files named in its own definition. Adding a file to an agent's read list
  is a harness change, reviewed like code.
- The Evaluator reads the **changed** files, listed by the Generator's summary, not the tree.
- Skill files have page budgets (see `.harness/skills/*/SKILL.md` headers). `app-context` is short
  because every agent pays for it; `coding-conventions` is longer because only the Generator does.
- Between sprints, context resets completely. Cross-sprint memory is `.harness/reviews/`, which the
  Monitor writes and only the Monitor and a human read.

---

## 7. Repository map

| Path | Purpose |
| --- | --- |
| `CLAUDE.md` | this file — orchestrator |
| `PROMPT.md` | the feature prompt used for the demonstration run |
| `DESIGN_BRIEF.md` | harness design brief |
| `REFLECTION.md` | reflection on the demonstration run |
| `DEPLOYMENT.md` | deployment target, steps, evidence |
| `JOURNAL.md` | architecture journal |
| `.harness/agents/` | the four agent definitions |
| `.harness/skills/` | feedforward skill files, one directory each |
| `.harness/output/` | working files during a run — gitignored |
| `.harness/reviews/` | archived sprint artefacts — committed, permanent audit trail |
| `src/` | StoreOps source: `Modules/{activities,programmes,staff,alerts,reports}`, `Shared/` |
| `tests/` | test project, mirroring the module structure |
| `tools/StoreOps.ArchCheck` | deterministic architecture gate (rules `SO-001`…`SO-007`) |
| `tools/StoreOps.CoverageGate` | deterministic per-layer coverage gate |
| `.github/workflows/ci.yml` | pipeline running the same four checks |
