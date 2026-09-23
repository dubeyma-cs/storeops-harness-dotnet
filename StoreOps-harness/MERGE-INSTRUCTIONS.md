# How to merge this folder into the StoreOps repository

This folder contains the harness, the documents, the CI workflow, the container definitions and
the coverage gate. It does **not** contain the application source — that is in the project folder
delivered separately (`src/`, `tests/`, `tools/StoreOps.ArchCheck`, `StoreOps.sln`,
`Directory.Build.props`, `.editorconfig`).

Every path here is already repository-relative, so merging is a copy.

## 1. Copy over the project folder

```powershell
# from the parent directory containing both folders
Copy-Item -Path .\New-fodt-harness\* -Destination .\New-fodt\ -Recurse -Force
```

```bash
# bash equivalent
cp -r New-fodt-harness/. New-fodt/
```

Nothing collides: no file in this folder exists in the project folder.

## 2. Add the coverage gate to the solution

`tools/StoreOps.CoverageGate` is a new project and must be registered:

```bash
dotnet sln StoreOps.sln add tools/StoreOps.CoverageGate/StoreOps.CoverageGate.csproj
```

## 3. Build and run the gates

```bash
dotnet restore StoreOps.sln
dotnet build StoreOps.sln --warnaserror
dotnet test StoreOps.sln /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
dotnet run --project tools/StoreOps.ArchCheck
dotnet run --project tools/StoreOps.CoverageGate -- \
  --report tests/StoreOps.Api.Tests/TestResults/coverage.cobertura.xml
```

## 4. Initialise git

```bash
git init
git add .
git commit -m "StoreOps API and development harness"
```

`.gitignore` ignores `.harness/output/` (except its README) and keeps `.harness/reviews/` —
the governance audit trail is meant to be committed.

---

## What still needs a verified run

Three things in this delivery are written against figures that must come from an actual run on the
assembled repository. Each is marked in place.

| Item | Where | What to do |
| --- | --- | --- |
| Test counts and `dotnet test` exit codes | `[UNVERIFIED]` rows in `.harness/reviews/sprint-1-generator-summary.md`, `sprint-1-evaluator-feedback.md`, `sprint-2-generator-summary.md`, `sprint-2-evaluator-feedback.md` | run `dotnet test StoreOps.sln` and replace the markers with the real results |
| Coverage figures | `.harness/reviews/sprint-1-coverage-gate.md` (template) and the `[UNVERIFIED]` coverage rows | run the coverage gate with `--md .harness/reviews/sprint-1-coverage-gate.md` to regenerate the report in place |
| Deployment screenshots | `DEPLOYMENT.md`, marked **[SCREENSHOT 1]** and **[SCREENSHOT 2]** | run `docker compose up --build -d`, capture `docker compose ps` and the `207` response |

The architecture-gate figures throughout (57 files scanned, 0 violations, and the module dependency
graph) are from a real run of `tools/StoreOps.ArchCheck` against `src/` and need no change.

One file in this folder is application code rather than harness material, and is required for the
sprint-2 artefacts to be accurate: `tests/StoreOps.Api.Tests/Alerts/HandoverNotificationTests.cs`
verifies AC-14, which no existing test covered.

---

## Contents

```
CLAUDE.md                                   orchestrator — entry format, agents, loop, escalation, CI, context
PROMPT.md                                   the demonstration-run prompt + curl walkthrough
DESIGN_BRIEF.md                             sections A–D
REFLECTION.md                               one page on the run
DEPLOYMENT.md                               local Docker (primary) + Azure Container Apps
JOURNAL.md                                  12 dated decision entries
MERGE-INSTRUCTIONS.md                       this file
Dockerfile                                  multi-stage; runs the architecture gate in the build
docker-compose.yml                          host 5000 → container 8080, sweeps enabled
.dockerignore
.gitignore                                  ignores .harness/output/, keeps .harness/reviews/
.github/workflows/ci.yml                    the same four gates + a container smoke test

.harness/agents/
  planner.agent.md  generator.agent.md  evaluator.agent.md  monitor.agent.md

.harness/skills/                            8 skill files
  app-context/                shared    modules, layers, endpoints, naming map, roster
  architecture-principles/    shared    SO-001..SO-007 with satisfying + violating C#
  sprint-decomposition/       planner   boundary heuristics, AC grammar, contract template
  coding-conventions/         generator C# 12, StyleCop ruleset, DI lifetimes
  api-integration/            generator routes, status codes, error envelope, events, batch checklist
  how-to-test/                generator assertion rules, fixed clock, isolation, coverage floors
  how-to-review/              evaluator procedure + 10 binary conversion recipes
  evaluation-criteria/        evaluator dimensions, weights, hard gates, verdict arithmetic

.harness/reviews/                           demonstration-run artefacts (committed)
  README.md                                 what the archive is, who reads it, how to query it
  spec.md                                   approved, 2 sprints
  sprint-1-contract.md                      AC-1..AC-12
  sprint-2-contract.md                      AC-13..AC-15
  sprint-1-generator-summary.md
  sprint-1-evaluator-feedback.md            iteration 1 FAIL → iteration 2 PASS
  sprint-1-run-log.md                       4 drift signals, token cost, trend
  sprint-2-generator-summary.md
  sprint-2-evaluator-feedback.md            PASS 97/100 with one scored finding
  sprint-2-run-log.md
  sprint-1-arch-check.md                    real tool output
  sprint-2-arch-check.md                    real tool output
  sprint-1-coverage-gate.md                 template — regenerate

.harness/output/README.md                   why this directory is gitignored

tools/StoreOps.CoverageGate/                per-layer coverage gate (new project)
  StoreOps.CoverageGate.csproj
  CoverageScopes.cs                         the four scopes and their floors
  CoberturaReader.cs                        line-level parsing, partial-class merging
  Program.cs                                CLI, text + markdown reports, exit 0/1/2

tests/StoreOps.Api.Tests/Alerts/
  HandoverNotificationTests.cs              AC-14
```
