# Step 1 re-verification - working Docker

Latest observed results supersede the historical blocked run below.

- Initial test exposed a real resource-mapping bug: the string destination was interpreted as a directory. Fixed with explicit Testcontainers FilePath mappings.
- `dotnet test --configuration Release`: exit 0; IntegrationTests 4 passed (including real PostgreSQL connectivity, schema isolation and repeatable migrations), Gateway.Tests 3 passed; no failures or skips. Domain.Tests intentionally empty at this point.
- `docker compose up --build --wait`: exit 0; both migration tasks exited successfully and PostgreSQL/API/gateway became healthy.
- Actual HTTP checks: ports 8080 and 8081 each returned 200 Healthy for /health/live and /health/ready with generated correlation IDs.
- `docker compose exec -T api id -u` and gateway equivalent: both 1654 (non-root).
- `docker compose down`: exit 0; services and network removed, data volume retained.
- GitHub execution/GHCR push and managed hosting remain unverified; no paid hosting was provisioned.

## Historical first run (Docker unavailable)

# Step 1 verification ? 2026-10-23

These are observed local results, not a deployment claim. SDK: 10.0.402. No step-2 business code exists.

| Check | Actual result |
|---|---|
| `dotnet restore Labaik.sln` | Succeeded for all eight projects. |
| `dotnet build Labaik.sln --configuration Release` | Exit 0; Build succeeded, 0 warnings, 0 errors. An initial EF patch-version conflict was corrected by explicitly aligning the runtime relational package with the design tools. |
| `dotnet test Labaik.sln --configuration Release` | Exit 1: six smoke cases passed, one PostgreSQL/Testcontainers test failed before a container could start; zero skipped. Domain test project has no cases yet. |
| API HTTP smoke | Three cases passed: live 200, unavailable-DB ready 503, business path 404; correlation format checked. |
| Gateway HTTP smoke | Three cases passed: live 200, unavailable-DB ready 503, business path 404; correlation format checked. |
| Real database proof | NOT VERIFIED: DockerUnavailableException; Docker API reports Docker Desktop is unable to start. Includes both services' up/down readiness, schema isolation, no implicit migrations and repeatable explicit migrations once Docker is available. |
| `dotnet list Labaik.sln package --vulnerable --include-transitive --format json` | Report generated; checker reports no known vulnerable packages, including transitive dependencies. |
| `docker compose config --quiet` | Exit 0. Configuration parses and interpolates with ignored generated .env. |
| `docker compose up --build --wait` | Exit 1, Docker Desktop unable to start; images/stack were NOT built or run locally. |
| EF idempotent migration scripts | Both generated offline; this does not prove execution against PostgreSQL. |
| Explicit API migration command with unreachable DB | Exit 1 with sanitized JSON migration-failure log; does not start HTTP or report success. |
| Actions syntax validation | actionlint v1.7.12 exited 0 for both workflow files. This is static validation, not a hosted run. |
| GitHub CI/GHCR and deployment | Not run; no Git remote configured and no hosting secrets/resources. Deployment jobs explicitly skip without configuration. |

Real terminal summary from the full test run:

```text
Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4, Duration: 1 s - Labaik.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 1 s - Labaik.Gateway.Tests.dll (net10.0)
```

The failing test is Both_services_are_ready_only_while_real_PostgreSQL_is_reachable, with DockerUnavailableException and Docker Desktop unable to start. This remains a required failing test locally, not a silent skip. CI requires Linux Docker and runs it without a skip switch. Raw local TRX results and generated migration/vulnerability reports are in ignored artifacts/; CI uploads its own results. Follow DEPLOYMENT.md to clear the blocker and deploy manually later. The requested commit message describes the intended deliverable; it is not evidence of a public deployment.

## Step 2a results

`dotnet build --configuration Release`: exit 0, 0 warnings/errors. `dotnet test --configuration Release --logger "trx;LogFilePrefix=step2a-final" --results-directory artifacts/tests`: exit 0; Domain.Tests 744 passed, IntegrationTests 7 passed, Gateway.Tests 3 passed; 0 failed/skipped. PostgreSQL migration/checks and stale-version tests ran against real containers. Initial constraint test failure was due to updating unrelated rows; fixed by binding its own booking ID. No identity/refresh behavior exists in this commit.

## Step 2b identity acceptance

`dotnet build --configuration Release`: exit 0, 0 warnings/errors. Final full `dotnet test --configuration Release --logger "trx;LogFilePrefix=identity-acceptance" --results-directory artifacts/tests`: exit 0.

```text
Passed!  - Failed:     0, Passed:   744, Skipped:     0, Total:   744, Duration: 622 ms - Labaik.Domain.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 1 s - Labaik.Gateway.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    26, Skipped:     0, Total:    26, Duration: 13 s - Labaik.IntegrationTests.dll (net10.0)
```

Identity tests use real PostgreSQL for concurrent same-key recovery, restart recovery, changed-key/late replay family revocation, consumed-child rejection, explicit provisioning, same-email UID isolation, JWT validation and shared account/IP limits. Six offline real Admin SDK cases replace only HTTP with local certificate/OAuth/revocation responses; signature/project/expiry (including just-expired)/revocation rejection logic executes unchanged. No live Firebase exchange or real project credentials were verified. Dependency vulnerability scan again reported no known vulnerable packages, including transitive dependencies; Compose config passed. No later business endpoints are enabled.

Post-identity container recheck: `docker compose up --build --wait` exited 0 with successful migrations and healthy PostgreSQL/API/gateway. All four HTTP health probes returned 200 Healthy; /bookings returned 404 and /auth/firebase returned 503 with no live Firebase/auth configuration (fail-closed, not a fake login). `docker compose down` exited 0. The final constant-time request-key comparison was additionally covered by the final local build/full test run; the Docker smoke verifies container/migration/route operation, not a live Firebase login.

## Step 3 - new payment suite only

User explicitly requested new tests only, with no full older-suite rerun. Observed commands:

```text
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter FullyQualifiedName~PaymentIntegrationTests --logger "trx;LogFilePrefix=step3-acceptance-new-only" --results-directory artifacts/tests
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28, Duration: 10 s - Labaik.IntegrationTests.dll (net10.0)

dotnet build --configuration Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

`docker compose config --quiet` and `git diff --check` exited 0. PostgreSQL Testcontainers applied both new migrations under distinct schema-owner roles; Core/gateway HTTP tests ran with separate restricted runtime roles. The new suite includes ONE 200-request concurrency case, not a rerun of the old domain/identity suites. Tests exercise real HTTP via TestServer, provider-host restart against durable data and deterministic lost-response/SQL-failure boundaries. They do not claim real card processing, literal OS process kill or a live HyperPay deployment.

Initial compilation caught deprecated .NET test-host APIs; used supported HostBuilder/TestServer wiring instead of suppressing warnings. First executed suite passed 24/25; the one failure was a test callback base URL targeting / instead of /webhooks/gateway. Fixed the URL, verified the failing case alone, then extended and passed the new suite (27, finally 28 cases) including final-failure stale-response protection. Old migration-count assertions were updated for the new migrations but those old suites were NOT rerun. No new packages were added, so no additional dependency restore/scan or Docker image/stack rebuild was performed beyond the build's normal cached restore. CI still runs its normal full verification after push; that external run remains unverified.

Core sale/capture/callback routes and Core payment-worker activation remain off pending step 4. Gateway authenticated simulation routes are available when configured; its dispatcher defaults off. Actual automatic refunds/capacity and downstream assignment/email/push integrations are not included in this result. Existing successful local Compose/identity results above are historical and are not fresh step-3 deployment evidence.

## Step 4 (two-hour timebox)

Focused `CapacityRefundTests`: 5 passed, 0 failed/skipped (21 s reported test duration). Real PostgreSQL tests proved 200 captures allocate exactly 50 slots and create/refund 150 overflow charges once; voucher reservations honor global/per-user limits under 200 concurrent attempts; cancellation races and lost refund responses recover. Release build passed with 0 warnings/errors before the focused run. Public booking/callback routes are now activated behind JWT/HMAC checks. Full regression deferred until completion of the timeboxed stages.

## Step 5
Release focused results: AssignmentTests 3 passed, 0 failed, 0 skipped (4 s). PrivateFileTests 1 passed, 0 failed, 0 skipped (7 s). Testcontainers used PostgreSQL 17 and Azurite 3.37.0. The initial missing Azurite image failed; the corrected pinned image passed. No live Azure deployment was tested.

## Step 6
Release build: succeeded, 0 warnings, 0 errors. PayoutTests: 6 passed, 0 failed, 0 skipped (5 s). Covers 20 concurrent same/different-key batch requests, proof/role guards, repeat export, failed-claim recovery, terminal result race and dispute exclusion. Bank execution/format acceptance is not verified.


## Step 7 / final regression (23 October 2026)

Observed full command:

```text
dotnet test --configuration Release --logger "trx;LogFilePrefix=final" --results-directory artifacts/tests
Passed! Failed: 0, Passed: 744, Skipped: 0, Total: 744 - Labaik.Domain.Tests (740 ms)
Passed! Failed: 0, Passed:   3, Skipped: 0, Total:   3 - Labaik.Gateway.Tests (3 s)
Passed! Failed: 0, Passed:  76, Skipped: 0, Total:  76 - Labaik.IntegrationTests (1 m 23 s)
```

Total: **823 passed, zero failed/skipped**. PostgreSQL and Azurite were real Testcontainers services; no in-memory database replacement. The full run includes all five financial/concurrency proofs plus identity and three NBomber 200-user scenarios. TRX artifacts are local ignored files, not evidence of a remote CI run.

After that full run, a stale-notification fix, per-instance heartbeat test and migration-history correction were checked with the focused command (not a second full run):

```text
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter "FullyQualifiedName~DeliveryOperationsTests|FullyQualifiedName~PayoutTests|FullyQualifiedName~CoreSchemaTests"
Passed! Failed: 0, Passed: 15, Skipped: 0, Total: 15 (5 s)

dotnet build --configuration Release --no-restore
Build succeeded. 0 Warning(s), 0 Error(s). (7.14 s)
```

The targeted run proves the final fresh-database migrations/constraints, weekly payout worker and final delivery/heartbeat behavior. Do not add overlapping focused counts to the 823 full-regression count.

Observed dependency scan:

```text
dotnet list Labaik.sln package --vulnerable --include-transitive --format json
python scripts/check-vulnerabilities.py artifacts/vulnerabilities.json
No known vulnerable packages reported, including transitive dependencies.
```

NBomber final full-run reports are copied into load/results. Each scenario reports 200 operations, 200 expected outcomes, zero unexpected failures. Expected voucher 409 conflicts count as correct responses: database assertions separately require 20 global or 2 per-user reservations. Capacity asserts 50 slots/200 captures/150 refund requests. These are correctness measurements through production HTTP TestServer endpoints, not a remote throughput benchmark; do not treat the reported nominal RPS as a production SLA.

Docker upgrade verification caught a local migration-history problem: an earlier local image had applied DeliveryOperations before its ID was regenerated for a new job kind. The deployment stopped with exit 1 rather than starting the API on an inconsistent schema. Fixed by preserving the originally applied migration ID and adding a separate WeeklyPayoutJobs migration; the retained local database is used for the retry. NuGet also retried a prematurely ended package download. Dockerfiles now separate project/dependency layers from source and share a locked package cache.

Final Compose retry/health/shutdown evidence follows. No live Firebase exchange/FCM, Azure cloud, Resend recipient delivery, bank transfer/CSV acceptance, public hosting, GHCR push or remote Actions run was performed. Local SDK contract tests/provider transport fakes do not establish live-provider acceptance.


Final retained-database retry observed at 15:47 UTC:

```text
docker compose up --build --wait
exit 0; postgres, api and fake-gateway Healthy; both migration tasks exited 0
API /health/live 200 Healthy; /health/ready 200 Healthy
Gateway /health/live 200 Healthy; /health/ready 200 Healthy
GET /operations without a token: 401
api id -u: 1654; fake-gateway id -u: 1654
Applied: preserved DeliveryOperations + new WeeklyPayoutJobs
Nightly Reconcile: Completed, zero mismatches on the empty local ledger
Weekly PayoutSweep: Completed, no available earnings

docker compose down
exit 0; services stopped/removed, network removed; database volume retained
```

No production database was contacted. The successful upgrade used the existing local volume and real schema-owner/runtime separation. This is a local deployment check; cloud integrations and managed hosting remain unverified. The final source is covered by the explicit build and focused 15-test follow-up, while the recorded full 823-test run predates the two follow-up tests/fixes. CI will run the complete current suite after push.
