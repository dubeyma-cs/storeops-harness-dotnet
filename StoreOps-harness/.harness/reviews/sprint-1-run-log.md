# Run log — sprint 1

| Field | Value |
| --- | --- |
| Sprint id | sprint-1 |
| Feature | Shift handover bulk status update — synchronous request contract |
| Verdict | PASS |
| Score | 100/100 |
| Iterations used | 2 of 3 |
| Escalation flag | false |
| Escalation trigger | — |
| Hard gates | 8/8 |
| Files changed | 9 (8 after the iteration-2 revert) |
| Tests added | 12 acceptance tests + 2 fixture helpers |
| Estimated token cost | ~52k (Planner 6k + Generator 14k × 2 + Evaluator 16k × 2 ÷ see note + Monitor 4k) |
| Wall-clock | 41 min, of which ~6 min waiting at the approval gate |

Token note: Planner 6k + Generator 14k + Evaluator 16k (iteration 1) + Generator 12k + Evaluator
14k (iteration 2, smaller because the diff was smaller) + Monitor 4k ≈ 66k. The 52k figure in the
table was the pre-run estimate; the actual is 66k, a 27% overrun caused entirely by the second
iteration. Recorded so the next sprint's estimate can use a realistic iteration multiplier.

## Iteration history

| Iteration | Verdict | Score | Checks that moved | Checks still failing |
| --- | --- | --- | --- | --- |
| 1 | FAIL | 65 | — | H2.2, C1.3, C1.5, C2.1, C2.2, C3.2 |
| 2 | PASS | 100 | H2.2, C1.3, C1.5, C2.1, C2.2, C3.2 | none |

Every check that failed in iteration 1 moved in iteration 2, and nothing regressed. That is the
shape a healthy loop makes: a feedback file with four concrete instructions produced four changes
and no collateral.

## Dimension trend

| Dimension | This sprint | Previous sprint | Direction |
| --- | --- | --- | --- |
| D1 Architecture conformance | 35.0 / 35 | — | first sprint |
| D2 Contract fulfilment | 30.0 / 30 | — | first sprint |
| D3 Test quality and coverage | 25.0 / 25 | — | first sprint |
| D4 Error contract and API consistency | 10.0 / 10 | — | first sprint |

## Indeterminate checks

| Check | Iteration | Why the Evaluator could not decide |
| --- | --- | --- |
| — | — | none |

## Skill file drift signals

| Signal | Evidence | Suggested skill file change |
| --- | --- | --- |
| The Generator implemented an auditable feature without testing the audit trail | F-1: AC-4 and AC-5 absent from iteration 1 entirely, even though both were in the contract | `how-to-test` already says "assert the side effect". Make it explicit that an **audit trail is a side effect** and name the pattern: read `GET /api/{resource}/{id}/audit` and assert one row per change. *(Applied — see `how-to-test` § "What an endpoint test must assert", item 4.)* |
| The Generator duplicated a rule in a controller | F-2 | `architecture-principles` stated `SO-004` but gave no example of the *correct* controller shape. Added the counts-only `switch` as a reference pass under `SO-004`. *(Applied.)* |
| The Generator made an unrequested formatting change | F-3 | `generator.agent.md` had no scope-discipline rule. Added: "does not improve the contract, extend the scope, or refactor code the contract does not mention", with the reason (the architecture gate scans everything that changed). *(Applied.)* |
| The Evaluator mis-scored C2.2 pro-rata before correcting itself | iteration 1 score stated as 79, corrected to 65 | `evaluation-criteria` now states "Each check is binary" in the scored-checks preamble and shows the dimension arithmetic explicitly. *(Applied.)* Watch for a repeat — a second occurrence means the wording is still ambiguous rather than merely missable. |

Four signals from one sprint is high, and expected: this was the harness's first run against this
codebase, so every skill file was meeting real Generator output for the first time. If sprint 2
produces a comparable number, the skill files are the problem rather than the feature.

## Notes

- The approval gate paid for itself. The Planner's design-decision table forced the 200/207/409
  question to be answered by a human *before* any code existed; the alternative is discovering in
  review that the Generator chose `200` for a partial failure.
- The two failing hard-gate paths agreed in iteration 1 — `H2.2` failed *and* the score was below
  80. That is reassuring for the framework's calibration, but the gate is what mattered: the score
  would have been 86 and a pass if the Generator had omitted only the AC-5 test.
- Iteration 2's Evaluator context was ~2k smaller than iteration 1's because it read only the
  changed files. Worth preserving: it is the mechanism that keeps a three-iteration sprint from
  costing three times a one-iteration sprint.
