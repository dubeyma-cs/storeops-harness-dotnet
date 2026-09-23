# `.harness/output/` — working directory

Gitignored. This is where agents write during a run:

| File | Written by | Lifetime |
| --- | --- | --- |
| `spec.md` | Planner | the run |
| `sprint-N-contract.md` | Planner | the run |
| `generator-summary.md` | Generator | one iteration — overwritten on the next |
| `arch-check.md`, `coverage-gate.md` | the gate tools | one iteration |
| `escalation.md` | orchestrator | until a human resolves it |

Clear it at the start of a run. The Monitor copies what matters into `.harness/reviews/` with a
`sprint-N-` prefix after every verdict, and that copy is what gets committed.

Why the split: these files are overwritten once per iteration. Committing them would fill the
history with churn and make the audit trail unreadable, while losing them entirely would leave no
record of *why* a sprint took three attempts. Archiving on verdict keeps both properties.
