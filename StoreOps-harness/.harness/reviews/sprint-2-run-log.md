# Run log — sprint 2

| Field | Value |
| --- | --- |
| Sprint id | sprint-2 |
| Feature | Shift handover bulk status update — handover notification via the event bus |
| Verdict | PASS |
| Score | 97/100 |
| Iterations used | 1 of 3 |
| Escalation flag | false |
| Escalation trigger | — |
| Hard gates | 8/8 |
| Files changed | 7 |
| Tests added | 1 (AC-14); AC-13 and AC-15 verified by existing tests |
| Estimated token cost | ~32k (Generator 13k + Evaluator 15k + Monitor 4k; no Planner invocation — sprint 2's contract was written in the same planning pass) |
| Wall-clock | 14 min |

## Iteration history

| Iteration | Verdict | Score | Checks that moved | Checks still failing |
| --- | --- | --- | --- | --- |
| 1 | PASS | 97 | — | C4.3 (scored, non-blocking) |

## Dimension trend

| Dimension | This sprint | Previous sprint | Direction |
| --- | --- | --- | --- |
| D1 Architecture conformance | 35.0 / 35 | 35.0 / 35 | flat at ceiling |
| D2 Contract fulfilment | 30.0 / 30 | 30.0 / 30 | flat at ceiling |
| D3 Test quality and coverage | 25.0 / 25 | 25.0 / 25 | flat at ceiling |
| D4 Error contract and API consistency | 6.67 / 10 | 10.0 / 10 | ▼ one check |

The D4 dip is the only movement, and it is informative rather than alarming: the check that failed
(`C4.3`) is about a contract the sprint did not own.

## Indeterminate checks

| Check | Iteration | Why the Evaluator could not decide |
| --- | --- | --- |
| — | — | none |

## Skill file drift signals

| Signal | Evidence | Suggested skill file change |
| --- | --- | --- |
| A notification's structured data was put into a prose string | F-1, `AlertService.cs:151`. The Generator followed `api-integration` faithfully — that file only describes **HTTP response** contracts | Add a short section to `api-integration` on notification payload shape: counts, ids and actors as fields; the body as display text derived from them. Until it exists, a Generator writing an alert has no rule to follow and will keep inventing sentences |
| Iteration count fell from 2 to 1 | sprint 1 used 2 iterations, sprint 2 used 1, on a sprint with a genuinely harder rule (`SO-002`) | No change. The four skill-file edits made after sprint 1 are the plausible cause; this is the trend the Monitor exists to show, and it points the right way |

One new signal, down from four in sprint 1 — the expected shape for a harness whose skill files
have just met real output for the first time.

## Notes

- The `SO-002` rule was satisfied on the first attempt, which is the single most useful data point
  in this run: it is the failure mode the client's standards team blocked the rollout over, and the
  sprint-1 edit to `architecture-principles` (adding the satisfying *and* violating code shapes,
  side by side) is the most likely reason. A rule stated abstractly gets violated; a rule shown as
  two code blocks gets followed.
- Sprint 2 needed no Planner invocation: both contracts came from the single planning pass, so the
  approval gate was paid for once across the whole feature. That is the intended economics of the
  one-gate design.
- Total run: 2 sprints, 3 agent-loop iterations, ~98k tokens, 55 minutes wall-clock, one human
  decision point. For comparison, the manual path the squad used before the harness was one
  developer-day plus a review cycle for a feature of this size.
- Outstanding recommendation carried out of this run: F-1 (structured notification payload), and
  gap 3 from the sprint-2 summary (no idempotency guard on the handover alert). Both are candidate
  sprints, neither blocks the feature.
