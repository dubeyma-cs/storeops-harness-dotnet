# Demonstration run prompt

This is the verbatim prompt used to invoke the harness for the demonstration run recorded in
`.harness/reviews/`. It follows the entry format defined in [`CLAUDE.md`](CLAUDE.md) § 1.

---

```
HARNESS RUN
Feature: Shift handover bulk status update

Intent: When a shift ends, the outgoing team needs to close out the activities they worked on in
one action instead of one request per activity. They should be able to mark multiple operational
activities as DONE or BLOCKED together. If some of those activities cannot be changed, the ones
that can must still go through — an outgoing shift should not lose forty good updates because the
forty-first activity was already closed by someone else. Every change must leave an audit record
of who made it, when, and why, so a store manager can reconstruct a handover during an incident
review. Store management should be told that a handover happened.

Constraints: The existing PATCH /api/activities/{id} behaviour must not change. Activities must
stay scoped to the caller's store. No new configuration may be required to run the app locally.

Out of scope: Undoing a handover. Changing priority, category or assignee in a batch. Email
delivery of the notification.
```

---

## What the harness produced from it

| Artefact | File |
| --- | --- |
| Specification (2 sprints, approved) | [`.harness/reviews/spec.md`](.harness/reviews/spec.md) |
| Sprint 1 contract — AC-1…AC-12 | [`.harness/reviews/sprint-1-contract.md`](.harness/reviews/sprint-1-contract.md) |
| Sprint 2 contract — AC-13…AC-15 | [`.harness/reviews/sprint-2-contract.md`](.harness/reviews/sprint-2-contract.md) |
| Sprint 1 generator summary | [`.harness/reviews/sprint-1-generator-summary.md`](.harness/reviews/sprint-1-generator-summary.md) |
| Sprint 1 evaluator feedback — FAIL then PASS | [`.harness/reviews/sprint-1-evaluator-feedback.md`](.harness/reviews/sprint-1-evaluator-feedback.md) |
| Sprint 1 run log | [`.harness/reviews/sprint-1-run-log.md`](.harness/reviews/sprint-1-run-log.md) |
| Sprint 2 generator summary | [`.harness/reviews/sprint-2-generator-summary.md`](.harness/reviews/sprint-2-generator-summary.md) |
| Sprint 2 evaluator feedback | [`.harness/reviews/sprint-2-evaluator-feedback.md`](.harness/reviews/sprint-2-evaluator-feedback.md) |
| Sprint 2 run log | [`.harness/reviews/sprint-2-run-log.md`](.harness/reviews/sprint-2-run-log.md) |
| Architecture gate reports | [`sprint-1-arch-check.md`](.harness/reviews/sprint-1-arch-check.md), [`sprint-2-arch-check.md`](.harness/reviews/sprint-2-arch-check.md) |
| Code | `src/StoreOps.Api/Modules/Activities/`, `src/StoreOps.Api/Modules/Alerts/`, `src/StoreOps.Api/Shared/Events/Contracts.cs` |
| Tests | `tests/StoreOps.Api.Tests/Activities/BulkStatusHandoverTests.cs`, `tests/StoreOps.Api.Tests/Alerts/HandoverNotificationTests.cs` |

## Calling the feature

With the app running on `http://localhost:5000` (`dotnet run --project src/StoreOps.Api`):

```bash
# 1. create two activities as the department lead
curl -s -X POST http://localhost:5000/api/activities \
  -H "Authorization: Bearer dev-token-department-lead" \
  -H "Content-Type: application/json" \
  -d '{"title":"Restock bay 4","department":"GROCERY","category":"RESTOCKING"}'

curl -s -X POST http://localhost:5000/api/activities \
  -H "Authorization: Bearer dev-token-department-lead" \
  -H "Content-Type: application/json" \
  -d '{"title":"Reset end-cap planogram","department":"GROCERY","category":"PLANOGRAM"}'

# 2. hand the shift over — substitute the two ids returned above
curl -s -i -X PATCH http://localhost:5000/api/activities/bulk-status \
  -H "Authorization: Bearer dev-token-department-lead" \
  -H "Content-Type: application/json" \
  -d '{"items":[
        {"activityId":"<id-1>","status":"DONE"},
        {"activityId":"<id-2>","status":"BLOCKED","reason":"Awaiting stock from the depot"},
        {"activityId":"act-not-real","status":"DONE"}
      ]}'
# → HTTP/1.1 207 Multi-Status
#   {"requested":3,"updated":2,"failed":1,"results":[…]}

# 3. the audit trail for an updated activity
curl -s http://localhost:5000/api/activities/<id-1>/audit \
  -H "Authorization: Bearer dev-token-department-lead"

# 4. the handover alert raised through the event bus
curl -s http://localhost:5000/api/alerts \
  -H "Authorization: Bearer dev-token-store-manager"
```

Bearer tokens are the seeded development roster described in
[`.harness/skills/app-context/SKILL.md`](.harness/skills/app-context/SKILL.md). They are not
credentials for anything outside this reference build.
