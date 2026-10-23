# Repository instructions

## Project and scope

- Labaik Core is the .NET 10 / PostgreSQL backend defined in docs/ASSIGNMENT.md. This is not an Expo or Firestore application.
- Read docs/ASSIGNMENT.md, this file, and docs/PLAN.md before implementation. User instructions take precedence over proposals in the plan.
- Current authorization: step 1 only, per the user's explicit go-ahead. Do not start step 2; report real checks and Docker/hosting blockers, then stop.
- Use Domain / Application / Infrastructure / Api boundaries and a separate fake gateway HTTP project. Gateway data lives in its own schema in the same PostgreSQL database; no shared transactions across the HTTP boundary.

## Interview-grade rules

- Prefer the smallest design that meets the assignment. Every entity, abstraction and dependency must have an explainable purpose. No generic repository, mediator, or framework added without a concrete need.
- Explain non-obvious decisions briefly in comments and document their trade-offs. Never claim exactly-once external effects from local database guarantees.
- Use database constraints, conditional updates and short transactions for invariants; no in-memory locks for cross-instance correctness.
- Use one booking state-machine component with explicit roles and guards; return 409 for invalid/stale transitions. Use a booking version concurrency token.
- Store money as integer minor units; compute prices on the server. Use database time for offer deadlines and UTC instants with an explicit business timezone for dates.
- Persist payment intent and idempotency keys before provider calls. Unknown outcomes stay unresolved; never open another charge attempt on timeout alone.
- Verify/decrypt webhooks, persist a deduplicated inbox, re-query the gateway, and validate amount/currency/reference before recording capture. Commit state and outbox together.
- Call external services outside DB transactions. Notifications only leave through the outbox. Document at-least-once delivery and retry behavior.
- Verify Firebase tokens with Admin SDK including revocation; identify by project and UID, never link or grant roles by email. Hash refresh tokens and commit reuse revocation before returning an error.
- Private file reads require ownership/role checks; signed uploads are exact-path and short-lived; approved evidence must remain immutable.
- Secrets only in environment variables; document names without values. No PII, tokens, card data or secrets in logs, prompts or fixtures.
- Hosted workers use unique scheduled JobRun rows and transactional claims with leases/fencing; retain business idempotency constraints. Do not introduce advisory locks without a demonstrated gap.

## Validation and workflow

- Follow the seven core steps in docs/PLAN.md. Once implementation is authorized, each step ends with passing relevant tests, documentation updates and one commit.
- Unit-test state machine, pricing and voucher rules. Use real PostgreSQL/Testcontainers for concurrency and failure tests; do not substitute an in-memory provider.
- Preserve the five required integration proofs: double payment, duplicate/out-of-order webhooks, 200-way capacity, accept vs expiry, and payout double-run. Also test refresh replay, Firebase identity separation, authorization and recovery.
- Run build and relevant tests after changes; CI includes the full suite and vulnerability scan. Report failed/unrun checks honestly. Documentation-only edits do not require application tests.
- Deploy health endpoints, Dockerfiles and CI in step 1. Never claim deployment or passing tests without observed evidence. Use expand/contract migrations, not destructive application-startup migrations.
- Keep optional XLSX/admin UI/telemetry dashboards outside the core build. Do not cut correctness or security to polish the demo.
- Append each prompt, proposal, user decision and actual validation to docs/AI_LOG.md. Update docs/DESIGN.md as implementation decisions and evidence become real.
