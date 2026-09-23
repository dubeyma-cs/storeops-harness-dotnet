# `.harness/reviews/` — governance audit trail

Everything in this directory is **committed**. It is the permanent record of what the harness did,
written by the Monitor after every Evaluator verdict and never edited afterwards.

`.harness/output/` is the opposite: working files for the run in progress, gitignored, cleared at
the start of the next run. The split is deliberate — working files churn once per iteration and
would drown the history, while this archive is what a reviewer, an auditor or the client's
standards team actually reads.

## What is here

| File | Written by | Contents |
| --- | --- | --- |
| `spec.md` | Planner (archived by Monitor) | the approved specification for the run, with its approval marker |
| `sprint-N-contract.md` | Planner | the sprint's acceptance criteria and definition of done |
| `sprint-N-generator-summary.md` | Generator | AC self-check table, files changed, known gaps |
| `sprint-N-run-log.md` | Monitor | verdict, iterations, token cost, trends, drift signals |
| `sprint-N-arch-check.md` | `tools/StoreOps.ArchCheck` | the architecture gate's own report |
| `sprint-N-coverage-gate.md` | `tools/StoreOps.CoverageGate` | the coverage gate's own report |

## How to read it

The chain of evidence for any sprint runs:

```
sprint-N-contract.md  →  sprint-N-generator-summary.md  →
        (what was agreed)        (what was built, self-reported)      
                                                    ↓
                                          sprint-N-run-log.md
                                              (what it cost)
```

Read them in that order. A claim in the summary that the feedback does not confirm is the most
interesting thing in the archive.

## Access

The directory is in the application repository, so read access follows repository access: the
eight-member squad, the client's standards team representative, and anyone reviewing a pull
request. Write access is the Monitor's during a run; nobody edits an archived artefact afterwards,
because its value is that it is what the agents actually wrote.

## Surfacing a recurring issue

The run logs are the primary input for detecting skill-file drift. Two queries answer most
questions:

```bash
# which architecture rules keep being violated?
 sort | uniq -c | sort -rn

# is the loop getting more expensive per sprint?
grep -h "Iterations used" .harness/reviews/*-run-log.md
```

A rule code appearing in two or more consecutive sprints means the skill file states the rule but
not the code shape that satisfies it — the fix is a skill-file edit, not another Generator
iteration. Rising iteration counts mean the skill files are lagging behind the codebase. Both are
recorded by the Monitor under *Skill file drift signals*, with the evidence that triggered them.
