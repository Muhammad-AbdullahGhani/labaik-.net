# Design

Status: steps 1, 2a, 2b and 3 implemented locally. Step 3 verification ran only the new payment suite at the user's request; previous regression results are historical. Core public payment routes/workers stay disabled until step 4. No later step is authorized.

## Architecture

Proposed: Domain / Application / Infrastructure / Api; separate fake gateway HTTP service using a restricted fake_gateway schema in the same PostgreSQL database. Core and gateway never share transactions. Hosted workers use durable row claims, leases and fencing, without advisory locks. The reduced entity model and seven core steps are in PLAN.md; document actual implementation and environment names (never values) as built.

## State machine diagram

Implemented domain transition/role graph below. Ownership, verified money and assignment guards remain application responsibilities before any future business endpoint is exposed.

```mermaid
stateDiagram-v2
    [*] --> Draft
    Draft --> PendingPayment: Customer or Admin
    PendingPayment --> Paid: System verified capture
    Paid --> Offered: System
    Offered --> Accepted: assigned Performer
    Accepted --> InProgress: assigned Performer
    InProgress --> ProofSubmitted: assigned Performer
    ProofSubmitted --> Completed: Supervisor or Admin
    Offered --> Paid: System expiry
    Draft --> Cancelled: Customer or Supervisor or Admin
    PendingPayment --> Cancelled: Customer or Supervisor or Admin or System
    Paid --> Cancelled: Customer or Supervisor or Admin
    Offered --> Cancelled: Customer or Supervisor or Admin
    Accepted --> Cancelled: Supervisor or Admin
    InProgress --> Disputed: Customer or Performer or Supervisor or Admin
    ProofSubmitted --> Disputed: Customer or Performer or Supervisor or Admin
    Completed --> Disputed: Customer or Supervisor or Admin
    Disputed --> Completed: Supervisor or Admin
    Disputed --> Cancelled: Supervisor or Admin
    Cancelled --> Refunded: System confirmed refund
```

Payment, refund, offer, and payout states are separate from booking state. A late charge on a cancelled booking creates a refund request; it does not resurrect the booking. Cancelling a paid booking retains its payment ledger and requests a refund.

## How races, double payment, and duplicate webhooks are prevented

Implemented: booking version checks, named money/capacity checks and transactional refresh-family locking. Future payment/assignment work: conditional capacity allocation, gateway idempotency, durable inbox/outbox and offer/payout transactions. Proposed mechanisms are in PLAN.md; those payment features are not claimed implemented.

## Failure scenarios tested

Latest runs include real PostgreSQL readiness/isolation, monetary constraint rejection, stale booking writes and identity replay races; all passed. The initial Docker-unavailable run is historical. Payment/offer/payout crash tests have not run because those features are not implemented. Record each future scenario, test name, observed invariant, and remaining limitation here after implementation. Planned scenarios include crash after charge, gateway timeout, webhook before redirect, payment after cancellation, capacity overflow, offer expiry, payout retries, and refresh-token replay.

## Trade-offs

Revised proposals: paid-only capacity allocation; refund on overflow; unresolved payments block a new attempt; at-least-once external delivery; PostgreSQL-backed hosted workers with row claims, no advisory locks; CSV only in core. Recommend encrypted same-successor refresh recovery; PLAN.md compares it with simpler new-child grace and explicitly describes the branching risk. Reduced tables give up multi-role accounts, invitation workflows and complete historical audit; financial ledgers remain. The user authorized 2a then 2b; stop after the identity commit.

## What I'd do next

After the required backend: real gateway contract validation, operational recovery exercises, stronger provider delivery deduplication, XLSX, and optional admin UI.

## How I used AI

See [AI_LOG.md](AI_LOG.md). Record accepted and rejected recommendations explicitly; proposals are not acceptance.

## Step 1 implementation and honest deployment status

Implemented the eight-project skeleton, pinned SDK and package versions, health-only HTTP hosts, schema-local empty EF migrations and explicit --migrate processes. JSON logs return a locally generated X-Correlation-ID; request logs contain status/duration and omit URLs, headers, IPs, bodies and provider exception details. Shared host/health plumbing is in Infrastructure; FakeGateway reuses that plumbing but never the app context. Domain/Application intentionally contain no business entities yet.

PostgreSQL uses app and fake_gateway schemas with separate migrator owners and restricted runtime logins. Bootstrap creates schemas/roles; each EF history table belongs to its schema. Default table/sequence grants permit future runtime DML only in its own schema. The Docker runtime is non-root; curl is installed only for image health probes. Compose runs explicit one-shot migrations before HTTP services, while normal startup never migrates, even in development. Readiness checks live DB access/schema USAGE, not applied migration version; rollout must separately require successful migrations.

No paid hosting is available: no Railway/Render target, no external deployment and no hosted health URL exist. CI verifies, scans packages and publishes immutable-SHA GHCR images; optional staging/production deployment jobs print a clean skip without hosting secrets. The future deployment hook is only a host-neutral client contract, not an implemented hosting adapter. See [numbered manual deployment steps](DEPLOYMENT.md) and [environment names](ENVIRONMENT.md).

Actual checks: Release build passed with 0 warnings/errors; six HTTP smoke cases passed. Full dotnet test exited 1 because Docker Desktop could not start the required PostgreSQL container (not skipped). Compose configuration and offline EF script generation passed; container build/run, live schema isolation/migration execution, GitHub execution/GHCR push and managed deployment remain unverified. Full evidence and commands are in [VERIFICATION.md](VERIFICATION.md). Do not present the database integration test as passing.

## Step 1 re-verification

With working Docker, corrected a Testcontainers file/directory mapping bug. Full tests passed (4 integration, 3 gateway); Compose built both images, completed explicit migrations, returned healthy on all four HTTP probes and ran both services non-root (UID 1654). Compose shutdown passed. Historical Docker-blocked results above are superseded locally; GitHub/GHCR and external hosting remain unverified. See VERIFICATION.md. User has authorized step 2a followed by step 2b, each in its own commit.

## Step 2a - domain core implemented

BookingStateMachine owns the explicit state/role table; unknown enums, self-transitions and all missing edges/roles return a failed RuleResult with a reason for a future API 409. Accountant has no booking transition privileges. Admin cannot manufacture Paid/Refunded or act as the performer. Booking state setters are private. Actor ownership, verified payment/refund evidence, assignment/deadline checks and transactional side effects remain application work; a role-table success alone is not sufficient authorization for those future operations. No business HTTP routes were added.

Money is a nonnegative SAR long value with checked addition/subtraction. PricingPolicy snapshots server configuration; date add-ons, fixed clamps and exact floor-rounded basis points produce immutable quotes. Percentage multiplication uses quotient/remainder to avoid overflowing an otherwise valid long result. Zero-total checkout is rejected. Voucher eligibility uses an explicit time and usage snapshot; atomic redemption/capacity allocation is not implemented yet.

BookingCore migration creates User, Booking, DailyCapacity, Voucher and VoucherRedemption with named checks/FKs/indexes. SQL numeric casts protect the total CHECK expression from bigint intermediate overflow; nullable discount shapes explicitly check IS NOT NULL because SQL UNKNOWN otherwise passes CHECK. EF versions increment on every tracked booking update while retaining the original token in WHERE; raw SQL/bulk writers must explicitly check/increment it. No generic audit or identity behavior was added.

Validation: Release build 0 warnings/errors; full tests 744 domain + 7 integration + 3 gateway, all passed, zero skipped. Includes every state/role pair, monetary/rounding boundaries, voucher expiry/limits, real migration/constraint rejection and stale writes through synchronous/asynchronous saves. An initial test updated unrelated bookings; scoped that invalid write to its own row before rerunning successfully. User then authorized completing step 2b; it is the only next scope.

## Step 2b - identity and replay protection

API exposes only health, Firebase exchange, refresh and authenticated /auth/me; gateway still exposes health only. IFirebaseTokenVerifier has a real Firebase Admin implementation (signature/project/issuer/expiry and checkRevoked=true), with stricter rejection after actual expiry rather than relying on SDK clock-skew allowance. Service-account credential loading explicitly restricts the credential type. Real verifier rejects emulator mode. Tests fake the verifier at the application boundary and also run the actual SDK against local test certificates, OAuth and revocation responses; this is not a live Firebase project check.

Users are UNIQUE(project,UID), never selected by email; Firebase claims cannot grant roles. Login inserts Customer only and preserves an explicitly provisioned role for an existing UID. --provision-user is a separate operator CLI that verifies the exact UID token before changing a role and revoking refresh families; no public role-grant endpoint exists. Operator possession of the server/DB credentials is its trust boundary.

JWTs carry sub/role/jti, expire in ten minutes and validate exact issuer/audience/HS256 signature/lifetime with zero local JWT skew. Refresh secrets have 256 random bits and only SHA-256 hashes are searchable in DB. Rotation locks RefreshFamily in PostgreSQL, then re-reads its token, consumes the parent and inserts exactly one successor (unique ParentId; composite FK prevents cross-family parenting). Store the complete first response encrypted with AES-GCM using parent ID as authenticated data. Same parent/request key within 15 database-clock seconds recovers the identical response; no grace extension or branch is minted. If that child was already consumed, reject recovery rather than resurrecting it. Changed-key/late replay revokes the family and commits before returning 401, including when the request disconnects after the security decision. Stateless access JWTs remain usable until expiry.

The family has an absolute 30-day lifetime; descendants never extend it. A five-second cleanup worker removes expired recovery ciphertext and old rate buckets with idempotent SQL; usable grace is exactly 15 seconds but physical ciphertext cleanup can lag by one timer interval. All replicas need the same recovery key. A thief with both parent and its exact retry key within grace remains indistinguishable from the client. Recovery/signing key rotation is documented operational work, not a hidden claim of transparent multi-key support.

Shared fixed-minute PostgreSQL upserts bound per-IP and verified-account requests, independently of refresh transactions. Keys are HMAC fingerprints, not stored raw IPs/emails. Only successful Firebase verification or possession of a stored refresh secret selects an account budget. There is no failed-login lockout by submitted email. Forwarded IP headers are ignored until a host-specific trusted-proxy policy is configured; ingress IPs conservatively share a budget meanwhile. Templates/notifications, money gateways and later assignment/payout flows remain unimplemented.

Identity acceptance: Release build 0 warnings/errors; final full suite 744 domain + 26 integration (including real PostgreSQL and six actual-SDK controlled-HTTP cases) + 3 gateway tests, 0 failed/skipped. Additional tests prove recovery does not resurrect a consumed child and explicit verified-UID role changes revoke old refresh families. No live service-account credentials or Firebase project were used; actual SDK cryptography/project/expiry/revocation logic ran against generated certificates and controlled HTTP responses.

Post-identity Compose build/migrations/readiness and shutdown passed locally; unconfigured Firebase exchange returned 503 and booking endpoint 404. Upstream Firebase revocation is checked on exchange/provisioning; already-issued local sessions have their own family lifecycle and are not continuously revalidated against Firebase. Live-project exchange/host ingress/trusted-proxy setup and external GitHub/GHCR remain manual verification items.

## Step 3 - checkout, gateway verification and durable delivery

PaymentCheckout locks the owned booking, freezes a server-generated request fingerprint and saves PaymentAttempt plus its recovery JobRun before any HTTP call. A partial unique index permits at most one Creating/Pending/Unknown/Succeeded attempt per booking. Repeated/new double-tap keys reuse it; same-key payload changes conflict. Provider errors/timeouts stay Unknown and block another attempt; only an authoritative final no-charge failure permits another key/attempt. Fingerprints use invariant formatting across replicas. The fake gateway persists idempotency-key/payload binding and a booking-wide unique charge, including across HTTP-host restart. A real provider must promise equivalent key/final-failure semantics; local uniqueness alone cannot stop two external charges.

Gateway checkouts, charges and encrypted events live in fake_gateway with its own restricted credentials and migration. HTTP operations require the shared API key. Simulation covers latency, response failure after commit, query outage, duplicate callback delivery and a configured browser redirect. Failed/Succeeded gateway outcomes are final; a failed checkout cannot later charge. Redirect target is configuration, never request input; redirect cannot mark Core paid.

Webhook envelope version 1 contains a version byte, fresh nonce, GCM tag and ciphertext; HMAC-SHA256 covers the entire raw envelope. Core bounds body size, verifies HMAC before decrypting, validates fields, stores the exact encrypted body/signature and deduplicates UNIQUE(provider,eventId) before acknowledging. Reusing an event ID with different wire bytes is quarantined. Provider retries must reuse the stored event body rather than re-encrypting it under the same ID. This is a documented simulation protocol, not a claim of real HyperPay wire compatibility or transparent multi-key rotation.

Workers re-query the authenticated gateway and check checkout/key/booking/amount/currency/transaction identity before credit. Gateway observations happen outside transactions; inside the commit transaction, job ownership and current booking/attempt state are re-read. Succeeded/Failed local attempts never regress on an older Pending response. Capture ledger, Paid transition/version, BookingPaid outbox intent and inbox acknowledgement commit together. BookingPaid is a durable integration event for future assignment/email/push handlers; those business integrations are not implemented here. Late payment on Cancelled records the capture plus RefundRequired/AccountantAlert intents without resurrecting the booking. Actual capacity allocation/refund execution is step 4, so public Core checkout/callback routes and payment worker activation remain disabled now.

JobRun claims use unique schedule keys, FOR UPDATE SKIP LOCKED, database-clock leases and increasing claim versions. Immediate per-entity jobs use a canonical scheduled slot; retries move AvailableAt rather than insert another schedule. Queries/sends run outside DB transactions. Final writes require the matching live owner/version under a row lock; crashed owners recover after lease expiry. Healthy pending-payment polling resets the error budget, while consecutive failures back off and dead-letter. Clearing EF tracked changes after a rolled-back business write prevents accidentally saving those failed inserts with the retry update. Generic receiver errors are stored as exception-type codes, never payload/secret details.

The outbox publishes over a private HTTP contract using a stable Idempotency-Key, then records acknowledgement under the job fence. Ambiguous acknowledgement can deliver twice; receiver deduplication is required. Gateway callback delivery likewise retries exact saved bytes with leases/backoff/dead letters. Background implementations exist, but Core production activation is intentionally withheld for step 4 and gateway dispatch defaults off. No advisory locks or new packages were added.

```mermaid
sequenceDiagram
    participant C as Customer
    participant A as Core
    participant D as Core DB
    participant G as Fake Gateway HTTP
    participant W as Worker
    C->>A: checkout retry (future public route)
    A->>D: save/reuse attempt and recovery job
    A->>G: create with saved key
    G-->>A: checkout or unknown response
    Note over A,G: timeout keeps same blocking attempt
    G->>A: signed encrypted callback (test adapter in step 3)
    A->>D: unique inbox and job
    A-->>G: accepted after commit
    W->>G: query current authoritative result
    W->>D: fence + capture + Paid + outbox + inbox acknowledgement
    W->>C: future integration event delivery via receiver
    Note over W,C: receiver must deduplicate repeated message IDs
```

Test evidence: 28 new PostgreSQL/HTTP payment cases passed; build 0 warnings/errors. Covers 200-way checkout/pay retries, same/different callback IDs, response-loss/restart, callback before create response/redirect, amount/currency/reference mismatches, forged/GCM-invalid/oversized input, query retry, forced outbox-write rollback, stale leases, lost publication acknowledgement, dead letters, final-failure races, late cancellation and disabled public routes. Fake HTTP hosts restart against the same durable DB; response-loss/storage faults are deterministic failpoints, not literal OS process kills. Full older suites, Docker image/stack rebuild and external providers were not rerun in this step, per the requested faster targeted workflow. See VERIFICATION.md and DEMO_PREP.md.

Ledger scope note: capture records are append-only in the supported application workflow, with no editing/deletion endpoints. No trigger forbids a privileged database owner from rewriting history; do not present that as a database-enforced immutable ledger. Financial uniqueness and state/counter checks are database-enforced separately.

## Step 4 implemented

Capacity is allocated only on verified capture using a bounded UPDATE on the date row, with booking allocation flag and capture/state/outbox committed together. Full dates cancel and create one refund per capture plus accountant alert. Cancellation releases an allocation once. Voucher checks/reservation counts run under the Voucher row lock; capture consumes a reservation, final no-charge failure/cancellation/refund releases it. An uncertain live attempt keeps its reservation until an explicit cancellation or authoritative outcome. Refunds use a stable key, match the full original amount/currency/transaction and only transition Cancelled to Refunded after provider confirmation. Fake gateway persists a unique refund per charge. Public creation/pricing/checkout/read/cancellation use authenticated ownership; browser return is read-only and callbacks require HMAC/AES-GCM. Five focused concurrency/failure cases passed; full regression will follow at the end of the approved timebox.

## Step 5 evidence
Assignment uses one short PostgreSQL mutex-row transaction, active-offer unique indexes and database deadlines. Acceptance and expiry use the same lock order. Three assignment tests passed, including concurrent accept/expiry and scheduler exclusion.
Private Azure Blob grants are exact-path, five-minute and create-only; approved content cannot be overwritten using the upload grant. One real Azurite test passed for private reads, ownership, path isolation and overwrite denial. Azure cloud credentials were unavailable; cloud deployment was not verified. Upload completion enforces size/content-type metadata; malware inspection and privileged storage-owner tampering remain outside this scope.

## Step 6 — completion and payout claims
Completion checks assigned performer, uploaded proof and supervisor-approved proof; one earning per booking commits with completion. Amount is a configured SAR fee capped to the booking total. Weekly batches include available completed earnings through the end of a closed UTC week, including older failed claims. A short shared SQL mutex serializes completion/dispute/payout decisions; this is conservative for low volume. Earning row claims plus partial UNIQUE(EarningId) for Pending/Paid payout items provide a second guarantee. Failed batches release claims; unknown bank outcomes remain Pending. Bank confirmation requires an explicit unique reference. CSV is deterministic, snapshots the configured checksum-valid Saudi IBAN on first export and does not send money. A dispute after export/payment needs accountant investigation, not automatic clawback. Bank-specific CSV certification remains external work.
