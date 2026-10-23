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
