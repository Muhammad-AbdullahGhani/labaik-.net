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
