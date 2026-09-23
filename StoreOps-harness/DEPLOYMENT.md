# Deployment

**Target chosen:** local Docker (primary, fully specified below) with an Azure Container Apps path
documented as the cloud option.

> **Evidence to attach before submission.** Two screenshots are required and cannot be generated
> from source: (1) `docker compose ps` showing the container healthy, and (2) the `207` response
> from the new `PATCH /api/activities/bulk-status` endpoint. The exact commands that produce them
> are marked **[SCREENSHOT]** below. If you take the Azure route instead, attach the live URL and
> the same `207` response against it.

---

## Why local Docker

StoreOps stores everything in memory, so there is nothing to provision: no database, no connection
string, no secret. A container is therefore the honest unit of deployment — it is the whole system.
The `Dockerfile` also runs the architecture gate inside its build stage, which means **an image
cannot be produced from a tree that violates `SO-001`…`SO-007`**. That property is worth more here
than a managed hosting plan: it puts the harness's primary governance rule into the artefact rather
than only into the developer's loop.

## Prerequisites

- Docker Desktop (or Docker Engine) with Compose v2
- Nothing else. No .NET SDK is needed to run the container; no configuration file has to be edited

---

## Local Docker — steps taken

### 1. Build and start

```bash
docker compose up --build --detach
```

The build runs in two stages: `sdk:8.0` restores, runs `StoreOps.ArchCheck`, and publishes;
`aspnet:8.0` receives the published output and runs as the image's non-root `$APP_UID`. The
architecture gate's output appears in the build log — if it exits non-zero the image is never
created.

### 2. Confirm the container is healthy   **[SCREENSHOT 1]**

```bash
docker compose ps
```

Expect `storeops-api` with state `running` and health `healthy`. The healthcheck polls
`/health`, the only anonymous route.

```bash
curl -s http://localhost:5000/health
# {"status":"healthy"}
```

Port mapping is `5000:8080` — the container listens on 8080 (`ASPNETCORE_URLS` in the Dockerfile),
and 5000 is used on the host so the container and `dotnet run` are interchangeable in every
command in this repository.

### 3. Confirm an authenticated baseline endpoint responds

```bash
curl -s -i http://localhost:5000/api/activities \
  -H "Authorization: Bearer dev-token-store-manager"
# HTTP/1.1 200 OK
# []
```

Without the header the same call returns the typed `401` envelope, which is the quickest proof that
the error contract is live:

```bash
curl -s http://localhost:5000/api/activities
# {"error":{"code":"UNAUTHENTICATED","message":"A valid 'Authorization: Bearer <token>' header …",
#  "statusCode":401,"traceId":"…"}}
```

### 4. Exercise the harness-built feature   **[SCREENSHOT 2]**

Create two activities, then hand the shift over with one deliberately bad id so the
partial-failure path is visible.

```bash
ID1=$(curl -s -X POST http://localhost:5000/api/activities \
  -H "Authorization: Bearer dev-token-department-lead" \
  -H "Content-Type: application/json" \
  -d '{"title":"Restock bay 4","department":"GROCERY","category":"RESTOCKING"}' \
  | python -c "import sys,json; print(json.load(sys.stdin)['id'])")

ID2=$(curl -s -X POST http://localhost:5000/api/activities \
  -H "Authorization: Bearer dev-token-department-lead" \
  -H "Content-Type: application/json" \
  -d '{"title":"Reset end-cap planogram","department":"GROCERY","category":"PLANOGRAM"}' \
  | python -c "import sys,json; print(json.load(sys.stdin)['id'])")

curl -s -i -X PATCH http://localhost:5000/api/activities/bulk-status \
  -H "Authorization: Bearer dev-token-department-lead" \
  -H "Content-Type: application/json" \
  -d "{\"items\":[
        {\"activityId\":\"$ID1\",\"status\":\"DONE\"},
        {\"activityId\":\"$ID2\",\"status\":\"BLOCKED\",\"reason\":\"Awaiting stock from the depot\"},
        {\"activityId\":\"act-not-real\",\"status\":\"DONE\"}
      ]}"
```

Expected response — this is the screenshot that shows the feature working:

```
HTTP/1.1 207 Multi-Status
Content-Type: application/json

{
  "requested": 3,
  "updated": 2,
  "failed": 1,
  "results": [
    { "activityId": "act-…", "outcome": "UPDATED", "status": "DONE" },
    { "activityId": "act-…", "outcome": "UPDATED", "status": "BLOCKED" },
    { "activityId": "act-not-real", "outcome": "FAILED",
      "errorCode": "RESOURCE_NOT_FOUND",
      "message": "Activity 'act-not-real' was not found in store 'store-042'." }
  ]
}
```

On Windows PowerShell, without `python`:

```powershell
$lead = @{ Authorization = "Bearer dev-token-department-lead" }
$a = Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/activities -Headers $lead `
  -ContentType application/json `
  -Body '{"title":"Restock bay 4","department":"GROCERY","category":"RESTOCKING"}'
$b = Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/activities -Headers $lead `
  -ContentType application/json `
  -Body '{"title":"Reset end-cap planogram","department":"GROCERY","category":"PLANOGRAM"}'
$body = @{ items = @(
    @{ activityId = $a.id; status = "DONE" },
    @{ activityId = $b.id; status = "BLOCKED"; reason = "Awaiting stock from the depot" },
    @{ activityId = "act-not-real"; status = "DONE" }
) } | ConvertTo-Json -Depth 5
Invoke-WebRequest -Method Patch -Uri http://localhost:5000/api/activities/bulk-status `
  -Headers $lead -ContentType application/json -Body $body |
  Select-Object StatusCode, Content
```

### 5. Confirm the two side effects the feature promised

The audit trail (one row per updated activity):

```bash
curl -s http://localhost:5000/api/activities/$ID1/audit \
  -H "Authorization: Bearer dev-token-department-lead"
# [ { "action":"CREATED", … , "source":"api.single" },
#   { "action":"STATUS_CHANGED","fromStatus":"TODO","toStatus":"DONE","source":"api.bulk-status",
#     "actorStaffId":"staff-002", … } ]
```

The handover alert, delivered through the event bus to store management:

```bash
curl -s http://localhost:5000/api/alerts \
  -H "Authorization: Bearer dev-token-store-manager"
# [ { "type":"SHIFT_HANDOVER","title":"Shift handover recorded",
#     "body":"2 of 3 activities were updated by staff-002 during handover (1 failed).", … } ]
```

That last response is the deployment-level proof of `SO-002`: `activities` never referenced
`alerts`, and the alert still arrived.

### 6. Stop and reset

```bash
docker compose down
```

State is in memory, so this is the reset. There is no volume and no database service —
anything that looked like persistence in `docker-compose.yml` would be misleading.

### Logs

```bash
docker compose logs -f storeops-api
```

Background sweeps are enabled in the compose file with shortened intervals
(`SlaSweepInterval` 1 min, `EscalationGracePeriod` 5 min) so the SLA-breach and escalation paths
are observable within a demo session rather than after four hours.

---

## Cloud option — Azure Container Apps

The same image runs unchanged. Steps, for a subscription where the resource group and Container
Apps environment do not yet exist:

```bash
RG=rg-storeops-capstone
LOC=uksouth
ACR=acrstoreops$RANDOM
ENV=cae-storeops
APP=storeops-api

az group create --name $RG --location $LOC

az acr create --resource-group $RG --name $ACR --sku Basic --admin-enabled true
az acr build --registry $ACR --image storeops-api:1.0 .

az containerapp env create --resource-group $RG --name $ENV --location $LOC

az containerapp create \
  --resource-group $RG \
  --name $APP \
  --environment $ENV \
  --image "$ACR.azurecr.io/storeops-api:1.0" \
  --registry-server "$ACR.azurecr.io" \
  --target-port 8080 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 1 \
  --env-vars ASPNETCORE_ENVIRONMENT=Production

az containerapp show --resource-group $RG --name $APP \
  --query properties.configuration.ingress.fqdn --output tsv
```

Then re-run steps 3–5 above against `https://<fqdn>` instead of `http://localhost:5000`.

Two constraints worth stating, because they are consequences of the in-memory design rather than
oversights:

- **`--min-replicas 1 --max-replicas 1` is mandatory.** Repositories are process-local singletons,
  so two replicas would serve two different datasets and the load balancer would decide which
  activity you see. Scaling StoreOps horizontally requires a real data store first.
- **A restart empties the store.** Container Apps may recycle a revision at any time. The seeded
  staff roster returns; activities, programmes, alerts and reports do not. Acceptable for a
  capstone reference build, and the reason `min-replicas` is 1 rather than 0 (scale-to-zero would
  wipe state between demos).

`az acr build` runs the same `Dockerfile`, so the architecture gate runs in the cloud build too — a
tree with an `SO-002` violation cannot produce a deployable image on either path.

---

## What CI already proves

`.github/workflows/ci.yml` has a `container` job that builds the image, starts it, polls `/health`
until it responds, and smoke-tests `GET /api/activities` with a seeded bearer token. Every push
therefore verifies the deployment path described above, independently of whether anyone ran it
locally.
