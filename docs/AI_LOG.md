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
