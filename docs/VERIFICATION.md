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
