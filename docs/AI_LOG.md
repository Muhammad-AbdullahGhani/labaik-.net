# AI assistance log

Record every subsequent prompt as a new entry. Never include credentials, tokens, or personal data. Link implementation commits and tests when they exist.

## Entry 001 — 2026-10-23 — Planning only

### Prompt

User requested reading docs/ASSIGNMENT.md and AGENTS.md first; no code; a plan for architecture/entities, six database-backed risk controls and tests, crash scenarios, test-and-commit build steps with deployment first, scope cuts, and documented ambiguities. User also requested DESIGN.md and AI_LOG.md skeletons and approval before implementation.

### AI proposal

Created PLAN.md with the requested design and acceptance criteria; DESIGN.md and this log are documentation skeletons. Proposed PostgreSQL constraints/transactions, persistent gateway idempotency, durable inbox/outbox, paid-only capacity allocation with automatic overflow refunds, atomic offer/payout handling, and refresh-token family locking. Found repository instructions at docs/Agents.md describing a different Expo app; proposed following the explicit .NET assignment for backend scope.

### User decision

- Accepted: plan approved in principle, subject to the changes in Entry 002.
- Changed: reduce entities, share gateway database via separate schema, reconsider locks/refresh choices, consolidate core steps.
- Rejected: incorrect Expo instructions; implementation remains explicitly unauthorized.
- Why: user requested a smaller design they can defend in an interview.

### Validation and artifacts

Read docs/ASSIGNMENT.md and docs/Agents.md. Documentation only; no application code, tests, deployment, or commits performed.

## Entry 002 — 2026-10-23 — Revision only

### Prompt

User approved with changes, explicitly forbidding implementation: delete wrong docs/Agents.md and create .NET/interview root rules; reduce entities and list cuts; compare encrypted-successor recovery with new-child grace and recommend an explainable design; share PostgreSQL via gateway schema; justify job locking vs Hangfire/Quartz; at most eight steps with steps 1–7 core, lean identity/files/notifications and no optional polish; add three-sentence spoken explanations for all six risks; wait for step-1 go-ahead.

### AI proposal

Replaced incorrect instructions with root AGENTS.md. Reduced/folded auxiliary entities with explicit lost capabilities. Kept separate gateway service but shared DB/schema isolation. Chose durable JobRun claims/leases/fencing instead of advisory locks, retaining business constraints. Grouped core into seven tested commits with deployment first and optional polish excluded. Compared both refresh choices; recommend A because it preserves one child, while literal B allows multiple valid children. Added six spoken explanations, including a 30-second identity recommendation; synchronized DESIGN.md.

### User decision

- Accepted: requested revision scope and original plan in principle.
- Changed: user required all changes listed in the prompt above.
- Rejected: original Expo instructions and implementation before a new go-ahead.
- Pending: review of this revision and explicit authorization for step 1; no acceptance of the AI's new recommendations inferred.

### Validation and artifacts

Documentation-only file and consistency checks; no application code, tests, deployment or commits. One intermediate documentation transformation failed before writing PLAN.md; corrected the transformation and verified the resulting document.

## Entry 003 ? 2026-10-23 ? Step 1 only

### Prompt

User authorized step 1 only: exact five source/three test projects, pinned .NET 10, health-only hosts, PostgreSQL app/fake_gateway schemas with empty EF migration and explicit migration runner, correlation/structured logs without PII, non-root Dockerfiles, one-command Compose, CI restore/build/Testcontainers/tests/vulnerability scan/GHCR, clean deployment skips without secrets, environment reference and numbered later deployment instructions. No paid hosting, no Railway/Render target, real build/test output required, one commit named chore: deploy tested service skeletons, then stop.

### Proposal and implementation

Implemented that skeleton without business endpoints/entities or step-2 identity. Two separate empty migrations and explicit --migrate image arguments keep production startup free of schema mutation. Distinct runtime/migrator schema credentials, generated ignored local secrets, JSON request fields with server-generated correlation IDs and sanitized database failures. Domain test project has no placeholder assertions. Added six HTTP smoke cases and one mandatory Docker test covering both services' real DB up/down behavior plus schema isolation, explicit migration repeatability and no implicit startup migration. Added Compose, GHCR workflows, optional synchronous deployment-hook contract and manual documentation.

### User decision

- Accepted: step-1 scope and the specified commit; implementation explicitly authorized this turn.
- Changed: user overrode paid-host deployment with no hosting and honest later manual steps; Docker unavailability must be reported, not treated as success.
- Rejected/out of scope: step 2 and all business endpoints, paid resources, fabricated passing tests/deployment.
- Pending: user review after commit; no further implementation authorization inferred.

### Real validation

Release build passed with 0 warnings and 0 errors after resolving an initial EF runtime/design patch conflict. Full dotnet test exited 1: six HTTP cases passed, one required PostgreSQL container case failed at Docker startup, zero skipped; Domain.Tests has no tests yet. Vulnerability report found no known vulnerable direct/transitive packages. Compose config passed; compose up failed because Docker Desktop cannot start. EF generated both empty migrations and offline idempotent SQL scripts; API --migrate against an unreachable DB returned nonzero with sanitized JSON. Actions workflow validation passed with actionlint v1.7.12. No containers, schema isolation, migration execution on real PostgreSQL, GHCR push or external hosting were verified. See VERIFICATION.md. Do not proceed to step 2.

## Entry 004 - Step 1 re-verification, then step 2a authorization

### Prompt

User reported working Docker and requested dotnet test --configuration Release, docker compose up --build --wait, then docker compose down; fix step 1, record actual results and commit chore: verify step 1 before continuing. Then authorize step 2a only: domain state machine/role table, SAR checked money/pricing, pure voucher rules, five EF entities with named constraints/migration and version concurrency tests. Explicitly exclude identity/refresh tokens; finish with feat: enforce booking and money invariants and stop.

### Proposal and user decision

Accepted scope: verify/fix step 1 first, then implement only the requested domain core. New schema includes User data but no login, token rotation or identity endpoints. Role and exceptional-transition choices will be documented and exhaustively tested. Authorization for later steps is not inferred.

### Step 1 findings (verification in progress)

The first working-Docker run exposed a real test setup bug: Testcontainers string/string resource mapping created a directory at the intended script filename. Replaced it with explicit FilePath source/destination mappings. Re-run passed: IntegrationTests 4, Gateway.Tests 3, failures 0, skipped 0; Domain.Tests has no cases before step 2a. Compose up --build --wait exited 0; all health URLs returned 200 Healthy; both containers ran as UID 1654; compose down exited 0. GitHub/GHCR and managed hosting remain unverified.

## Entry 005 - Step 2a completed before identity

User's follow-up requested step 2b identity while the step-1 verification was finishing. Asked for ordering because step 2a was still unimplemented; user explicitly answered to complete step 2a then move to step 2b. Implemented only domain core in this commit: state/role table, SAR arithmetic/configured pricing, voucher rules, five entities, named schema constraints and managed booking versions. Documented role decisions and application guards still needed, with Mermaid diagram. Tests: 744 domain, 7 integration, 3 gateway passed, 0 failed/skipped; Release build 0 warnings/errors. Corrected one constraint test that initially touched unrelated bookings, then reran full suite. Accepted: requested 2a scope and continuation into 2b; not accepted/inferred: any later business feature.

## Entry 006 - Step 2b identity

### Prompt and decision

User requested identity only: real Firebase Admin SDK behind IFirebaseTokenVerifier plus fake, UID/project uniqueness and Customer-only creation, explicit verified-UID staff provisioning without public grants/email linking, ten-minute JWT, design-A rotating hashed refresh tokens with family lock/one child/15-second encrypted recovery and committed replay revocation, safe shared IP/account throttling, auth plus health endpoints only, real Testcontainers results and one feat: secure identity and token rotation commit. User clarified to finish 2a first, then 2b. Accepted scope excludes every later booking/payment/offer/payout endpoint.

### Implemented proposal

Added Application verifier contract, real SDK adapter and fake in tests; schema-local refresh families/tokens and rate buckets with named constraints/indexes, PostgreSQL row locking and unique parent. AES-GCM recovers the identical original response, never another child. Replay revocation commits before returning unauthorized. Cleanup purges expired ciphertext and stale buckets idempotently. JWT validates issuer/audience/HS256/lifetime. Explicit --provision-user verifies exact UID then grants role/revokes families; no email selector/public grant. Shared HMAC-keyed DB budgets apply to socket IP and verified identity, not caller email. Documented conservative proxy behavior, absolute family lifetime, ciphertext cleanup lag, token/client/key-rotation requirements and live-provider limits.

### Real validation

Final full tests: 744 domain, 26 integration, 3 gateway, all passed; 0 failed/skipped. Real PostgreSQL tests include refresh concurrency/restart/replay/provisioning and schema/concurrency regressions. Six real Firebase SDK cases use generated certificates and controlled HTTP (no live credentials) to verify signature/project/expiry/revocation behavior. Build passed with 0 warnings/errors. Fixed initial compile mismatches against the current SDK (typed CredentialFactory instead of deprecated FromJson, actual AuthErrorCode names and HttpClientFactory type). Added strict post-verification Firebase expiry to remove SDK leeway for issuing fresh local sessions. Vulnerability scan clean. Live Firebase/managed hosting and GitHub/GHCR remain unverified. Post-identity Compose build/up passed with both migration jobs and four healthy HTTP probes; booking route 404, unconfigured Firebase exchange 503; down passed. Final constant-time retry-key comparison and two extra security tests passed in the final local run. No later scope is authorized.

## Entry 007 - Step 3 payments and demo preparation

### Prompts and accepted scope

User asked to speed up and start step 3, then asked to continue. During work the user explicitly requested only the NEW test suite rather than rerunning the older tests, and asked for maintained demo preparation in easy language explaining the product, problems/fixes, decisions and presentation. Accepted those constraints; did not infer authorization for step 4 or a full regression run.

### Proposed and implemented

Implemented durable fake-gateway HTTP checkout/pay/query/redirect with shared API-key authentication, persistent idempotency and charge constraints, configurable response loss/latency/query outages/duplicate delivery. Added core intent/unknown recovery, signed AES-GCM callback inbox, authoritative gateway verification, atomic capture/Paid/outbox commits, and durable claim/lease/fencing/retry/dead-letter workers and HTTP publication. Customer/capture/callback routes and worker activation remain disabled in Core until capacity/refunds; late captures are retained with compensation/alert intents for step 4. Kept downstream event publication separate from actual email/push/assignment work. Added DEMO_PREP.md early and updated it with honest demo wording and limitations. No new package/delegation/polish work.

### Real evidence and corrections

Release build passed with 0 warnings/errors; 28 NEW payment integration cases passed, 0 failed/skipped, including the required 200-way checkout/charge race. Used PostgreSQL containers, restricted schema roles and separate HTTP hosts. Updated deprecated test host APIs to supported HostBuilder. Corrected callback base URL after first executed 24/25 run. Added protection/test against a delayed Pending observation reopening a finally Failed attempt. Tests verify response-loss/restart, duplicate/out-of-order callbacks, malformed/mismatched data, atomic rollback, provider re-query outage, worker fencing, duplicate publication and dead letters. Compose config and diff checks passed. Older suites, new Docker image run, external CI/GHCR and real providers were NOT rerun/verified. Commit requested by the approved plan: feat: recover and verify idempotent payments. No step 4 implementation authorized.

## Entry 008 - Approved two-hour completion through step 7 / step 4

User approved the proposed two-hour timebox through steps 4-7, focused tests per stage and one final full regression run. This supersedes the older stop-before-step-4 instruction. Priorities: money/concurrency first, no optional UI/XLSX, plain-language demo notes and honest incomplete/live-provider limitations. Step 4 implements conditional capacity allocation, voucher-row-locked reservations/consumption/release, cancellation cleanup and durable idempotent full refunds. Public authenticated booking/create/checkout/cancel/read and signed callback routes are now mapped; workers remain a configurable activation setting. Five focused PostgreSQL tests passed, including 200 verified captures -> 50 paid / 150 refunded, 200-way global/per-user voucher limits, cancellation/capture race and lost-refund-response recovery. Build passed. Older route/migration assertions updated for activation; final regression reserved for the end. Next authorized work is lean assignment/files.

## Entry 009 — Step 5 under the approved timebox
Prompt: finish the remaining core steps in two hours, maintain easy demo wording and use focused tests before one final regression.
Proposed/accepted: ordinary row mutex for round-robin and deadline decisions; Azure private blobs with create-only grants; no upload UI or transcoding. Implemented assignment, suspension, file authorization and approval.
Validation: AssignmentTests 3 passed; PrivateFileTests 1 passed (real PostgreSQL and Azurite). Initial Azurite 3.35 pull failed; switched to available 3.37.0 and reran successfully. Cloud Azure remains unverified.

## Entry 010 — Step 6
Prompt/decision: continue the approved lean timebox; prioritize payout double-run before polish.
Implemented completion guards, single earning, weekly claims, terminal bank outcomes, retry after confirmed failure and deterministic CSV. Chose one short existing database mutex for explainable cross-workflow lock order; no advisory locks or new scheduling framework. Unknown bank status cannot release claims.
Validation: Release build succeeded, 0 warnings/errors; PayoutTests 6 passed, 0 failed/skipped (5 s), real PostgreSQL. No real bank transfer was attempted. Generated migration using a design-only connection; no production migration ran.


## Entry 011 - Finish the approved timebox
Prompt: the user had two hours and approved streamlining the remaining core steps; keep demo preparation easy to explain. Accepted: lean steps 4-7, focused checks during each stage, one final full regression, no optional UI/XLSX/paid hosting.
Implemented: internal durable paid/offer/completed routing, recipient outbox jobs, Firebase Admin registration-token adapter, Resend HTTP adapter, Arabic/English text, invalid-device removal, nightly reconciliation, weekly payout schedules, heartbeat/storage readiness, explicit PII-free OpenTelemetry spans and an accountant operations endpoint. Email recipients remain trusted operator mappings. No live-provider credentials were available, so local transports verify routing; real delivery is not claimed.
Validation: final full Release run passed 744 Domain + 76 Integration + 3 Gateway = 823 tests, zero failures/skips. Three NBomber checks each ran 200 concurrent users and passed their database invariants. The dependency scan reported no known direct/transitive vulnerabilities. A final focused delivery test covers suppressing delayed paid/expired offer notifications after state changes. See VERIFICATION.md for its result and the final Docker stack checks.
Changed/rejected: no generic scheduler framework/advisory locks; no silent outbox no-op for unknown event kinds; no treating CSV export as payment. Basic explicit trace spans avoid exporting signed queries or personal fields. Kept the SDK's supported registration-token field under one documented obsolete-warning suppression rather than incorrectly passing a token as an installation ID. Cloud deployment and bank certification are explicitly outstanding. Normalized mixed Windows Markdown encodings to UTF-8 so interview notes render correctly.

Final follow-up evidence: 15 focused tests passed after the migration/stale-delivery changes; explicit Release build passed with zero warnings/errors. The retained-database Compose check exposed an already-applied migration ID replacement; preserved that original ID and added a new additive job-kind migration instead. No data volume was deleted or migration history manually falsified. The failure and retry are documented rather than hidden. Docker dependency caching was improved after repeated slow package restores.

Final observed Compose result: exit 0 with both migrations and healthy API/gateway/PostgreSQL; all four live/ready HTTP checks returned 200, unauthenticated operations returned 401, both images ran as UID 1654. Retained-database history includes the preserved DeliveryOperations and additive WeeklyPayoutJobs. Nightly/weekly jobs completed. docker compose down exited 0, retaining the data volume. No remote publication/provider delivery is claimed.

## Entry 012 - Interview questions before Firebase configuration

Prompt: explain likely interview questions/concepts and add them to demo preparation before moving to Firebase setup.
Accepted scope: documentation only; Firebase configuration remains the next task, not performed now. Added 27 question groups with plain-language answers, deeper follow-ups, code/test pointers, a database-concept table, .NET lifetime/async/EF explanations and an eight-item rehearsal checklist. Corrected the older refund answer to describe the implemented worker instead of a future step. Answers distinguish local guarantees from provider contracts and historical full regression from focused follow-up results. No additional user acceptance/change/rejection of these proposed answers has been received yet.
Validation: checked the explanations against current queue, identity, assignment and EF versioning code; reviewed Markdown diff. Application tests were not rerun because only documentation changed. No secrets, credentials or Firebase configuration were read or modified.

## Entry 013 - Live Firebase configuration and demo guidance

Prompts: guide Firebase configuration, diagnose API 503 after Firebase sign-in, and explain what to showcase. User configured Firebase/service-account environment variables and started healthy Docker services. Read-only container checks printed only presence/validity booleans: Firebase configuration was present and consistent, but local Auth signing/recovery keys were missing. Generated only missing/empty local auth keys in ignored .env; preserved existing nonempty settings and did not print credentials. User must recreate API in their Firebase-configured PowerShell session and retry token exchange; no live exchange success is claimed yet. Added the exact demo sequence and failure story to DEMO_PREP.md. No application source changed or regression tests were needed for this configuration/documentation update.

## Entry 014 - Fix the remaining live-auth initialization failure

Prompt: token exchange still returned 503 after API recreation; authenticated /auth/me then returned 401. The 401 is downstream of the failed exchange, not a separate successful session. Container checks confirmed independent valid auth keys and matching Firebase credential/project configuration. Certificate and OAuth hosts were reachable. A deliberately invalid diagnostic token also returned 503, narrowing the failure to initialization.
Added logs containing only fixed initialization stages, exception types, SDK error enums, SQL state and code-origin type; no exception messages, response bodies, tokens or credential values. Observed AppCreation/ArgumentNullException for HttpClientFactory. Root cause: the adapter assigned null to AppOptions.HttpClientFactory in production; SDK rejects null. Tests supplied a nonnull factory and missed the default path. Changed code to set the property only when an explicit factory is supplied, preserving the production SDK default. Added a default-factory malformed-token regression.
Validation: the initial FirebaseAdmin filter matched no tests; corrected to FirebaseAdapterTests. Seven adapter tests passed, zero failed/skipped (2 s), including the new production-default case. Explicit Release build passed with zero warnings/errors (7.97 s). Only the affected suite ran; the full suite was not repeated. Rebuilding API while preserving Firebase environment configuration; real user-token exchange still awaits user retry.

Container verification completed: API rebuild/recreation exited 0 with preserved configuration, readiness Healthy, malformed-token rejection 401 (previously 503). Awaiting the user's real-token retry; no live successful session is asserted.

## Entry 015 - Observed live Customer login and repeatable demo guide

Prompt: user shared successful POST /auth/firebase followed by GET /auth/me displaying a local userId and Customer, and requested a demo guide. Recorded the user-observed real Firebase/local Docker login success without retaining their identifier or credentials. Replaced the pending status in DEMO_PREP.md and added private pre-demo setup, copyable login/exchange/me commands, expected results, narration, a live unauthenticated-check command, likely follow-ups and explicit limits. Local session variables are cleared before exchange and failures stop the sequence so stale tokens cannot masquerade as a new login. Updated verification/design evidence; no application code changed, no tests rerun. No new acceptance/rejection of the written guide has been received yet.

## Entry 016 - Copyable booking/checkout presentation

Prompt: user observed a Draft SAR booking and successful checkout identifiers, then asked whether commands can be copied from demo notes while explaining the flow. Added booking, checkout, stable-key retry comparison and booking-status blocks with narration and expected numeric state meanings. Recorded only observed outcomes, without the user's identifiers; repeat-checkout and simulated payment for this live booking remain unobserved. Documentation only; no application tests rerun. A frontend/Postman collection is not required for this command-led demo.

## Entry 017 - Prepare live Azure/FCM/email setup

Prompt: make live Azure configuration, FCM push and email work for the demo. Inspected current storage/notification adapters, routes and Compose mappings; checked official provider setup documentation. Requested missing external prerequisites: Azure access/credits, Resend/domain availability and whether a push recipient already exists. Proposed a local browser receiver if no app is available and the Resend own-account testing sender if no custom domain is available. These choices await the user's answers. Added an honest pending-provider milestone to DEMO_PREP.md; no cloud resource, paid service, provider send or live-success claim was made. No application code changed or tests rerun in this preparation.

## Entry 018 - Keep Azurite and prepare real notification receipt

Prompt/accepted decision: user deferred live Azure/trial and chose Azurite, asking to finish email/FCM first. Prepared a zero-dependency loopback browser FCM recipient with public Web app/VAPID configuration, masked copyable token and foreground receipt list. Its service worker initializes on restarts and leaves background notification display to Firebase to avoid duplicates. Public configuration validation rejects Admin/private-key fields, assets are allowlisted, the server binds loopback and does not log paths. Added a private PowerShell Resend setup helper: verifies the current local session, prompts for the own-account recipient and hidden sending key, preserves Firebase environment and recreates the API with an ignored recipient override. No role grant, direct provider send or notification endpoint bypass was introduced. Added safe provider error categories to the existing transport.
Validation: Release build succeeded, zero warnings/errors (6.11 s). NotificationTransportTests plus DeliveryOperationsTests: 9 passed, zero failed/skipped (17 s), including stable email idempotency payload, Arabic content and failure statuses. Two Python receiver safety/HTTP tests passed (0.872 s); PowerShell parser reported valid syntax; Node syntax checks passed for both browser scripts. CI now includes the receiver safety tests. Full .NET regression was not repeated. Browser permission/token acquisition, Resend account setup and actual live receipts remain pending user action. No provider credentials, real recipient addresses or tokens were committed, and no actual message was sent in these checks.
