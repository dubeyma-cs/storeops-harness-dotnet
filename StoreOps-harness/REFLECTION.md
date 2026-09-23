# Reflection — demonstration run

Feature: shift handover bulk status update. Two sprints, three agent-loop iterations, one human
decision point, ~98k tokens, 55 minutes wall-clock. Both sprints passed; sprint 1 needed a second
iteration.

## What the harness did well

**It caught the thing a human reviewer would have missed.** Sprint 1 iteration 1 implemented the
audit trail correctly — the rows were written, the endpoint exposed them — and shipped no test for
it. A reviewer reading that diff sees working audit code and moves on. The `H2.2` gate is
mechanical: twelve acceptance criteria, ten self-check rows, fail. The gap was found by counting,
not by insight, which is the only way it gets found every time.

**Making the contract the unit of work changed what the Generator argued about.** With AC-2
specifying `207`, `updated: 1`, two named error codes and *"the `TODO` activity reads back as
`DONE`"*, there was nothing left to interpret. The one place the Generator did improvise — filtering
the handover status set inside the controller — is exactly where the contract was silent, and the
`R-1` recipe caught it.

**Showing the violating code shape, not just the rule, moved `SO-002` from a risk to a non-event.**
After sprint 1, `architecture-principles` gained satisfying *and* violating C# side by side for
each rule. Sprint 2 introduced a cross-module notification — the failure mode the client's
standards team blocked the rollout over — and satisfied the rule on the first attempt, with the
module dependency graph unchanged. A rule stated abstractly gets violated; a rule shown as two code
blocks gets followed.

**The deterministic gates made the verdict boring, which is the point.** `StoreOps.ArchCheck` prints
the same graph and the same seven zeros every run. Forbidding the Evaluator from re-deriving a
tool's answer removed the largest remaining source of run-to-run variation, and the two failing
paths in iteration 1 agreed independently: the hard gate failed *and* the score was below 80.

## Where it fell short

**The Evaluator mis-scored its own framework on the first attempt.** Iteration 1 initially recorded
79 by scoring the AC-coverage check pro-rata — ten of twelve criteria had tests, so 0.83 of a
check. `evaluation-criteria` defines checks as binary. The verdict was unaffected (both numbers are
below 80), but the recorded *reason* for the failure would have been wrong, and a run log is only
as useful as its accuracy. It suggests the non-determinism strategy needs its own tests, not just
its own document.

**Four drift signals from one sprint is a lot.** Every one was a real gap — an audit trail is a side
effect, here is the correct controller shape, do not refactor outside scope, checks are binary —
and each became a specific skill-file edit. But four signals means the skill files were written
against an imagined Generator rather than a real one. The fix is not better authoring; it is
accepting that the first run against any codebase is partly a skill-file calibration exercise, and
budgeting for it.

**Token cost overran the estimate by 27%.** Estimated 52k for sprint 1, actual 66k, entirely from
the second iteration. The per-agent figures in `CLAUDE.md` § 6 are per *invocation* and the estimate
assumed one each. Iteration count is the dominant term and the estimate did not model it.

**Nothing here exercised the escalation path.** Zero indeterminate checks, no sprint reached three
iterations. The escalation output format and its recipient routing are designed and documented but
unproven — the part of this harness I would trust least.

**Both sprints were small enough to hide a scaling question.** Twelve and three acceptance criteria.
The Evaluator reads the changed files, so its context grows with the diff; a twenty-file sprint
would test whether ~16k holds, and I do not know that it does.

## One concrete improvement

**Make the AC-coverage gate structural instead of name-matching.** Today `H2.2` resolves the
`Verifying test` column of the Generator's self-check table against `dotnet test --list-tests`. It
works, and it caught the real defect — but it depends on the Generator writing the table honestly,
and the table is the one artefact in the loop with no independent check on it.

The fix: an `[AcceptanceCriterion("AC-4")]` attribute on each test, and a third gate tool that
reflects over the test assembly, builds the AC → test map from the attributes, and diffs it against
the contract's AC ids. The self-check table becomes a report rather than evidence, and the gate
reads the same source of truth the test runner does.

It is a small tool — mostly `Assembly.GetTypes()` and a set difference — and it closes the last
place in the harness where a hard gate trusts a claim rather than measuring a fact.
