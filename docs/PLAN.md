# Proposed implementation plan

Status: steps 1, 2a, 2b and 3 implemented locally; step 3 ran only its new payment suite at user request. No step 4 work authorized. External CI/registry/hosting and live Firebase exchange remain unverified. This document proposes behavior; it does not claim implementation or passing tests.

## 1. Structure and transaction rules

```text
src/Labaik.Domain                 entities, money, state machine, transition/role rules
src/Labaik.Application            use cases, interfaces, transaction orchestration
src/Labaik.Infrastructure         EF Core/PostgreSQL, Firebase, blobs, gateway client, workers
src/Labaik.Api                    endpoints, authorization, composition, health checks
src/Labaik.FakeGateway            separate HTTP service, persistent idempotency/charge ledger
tests/Labaik.Domain.Tests         state machine, pricing, voucher rules
tests/Labaik.IntegrationTests     real PostgreSQL via Testcontainers, API/worker tests
tests/Labaik.Gateway.Tests        gateway contract, crypto and failure simulation
load/                            k6 scenarios, assertions, reproducible instructions
```

Domain depends on nothing else. Application depends on Domain. Infrastructure implements Application interfaces. Api wires them together. Keep workers in Infrastructure, hosted by Api initially; no extra worker service needed. Use .NET 10 and EF Core migrations. No generic repository or mediator required.

All booking transitions pass through one component with an explicit role/guard table. Use a bigint Version concurrency token, incremented on writes and checked in update predicates; stale writes return 409. SQL locks protect multi-row invariants; a version alone does not protect capacity or external money. Keep network calls outside DB transactions. Persist intent before calling a provider, then conditionally apply the result in a fresh transaction. Use database time for deadlines. Define a consistent lock order per workflow, keep transactions short, and retry deadlocks/serialization failures only through idempotent operations.

### Reduced entity model

All IDs are UUIDs unless stated. Timestamps are UTC timestamptz; service dates are date in an explicitly configured business timezone. All money is bigint minor units with checked arithmetic and currency stored alongside it. Foreign keys default to restricted deletion for financial records; performerId now references User. Each table has a primary key; the following lists additional important constraints/indexes. Use explicit named constraints so conflicts can be translated intentionally.

| Entity | Main fields | Key constraints / indexes |
|---|---|---|
| User | Id, FirebaseProject, FirebaseUid, role, status, locale, suspendedAt, lastOfferedSequence, createdAt | UNIQUE(project,uid); one role/account; index(role,status,lastOfferedSequence); performer fields live here; email never an identity key; privileged roles explicitly provisioned against verified UID |
| RefreshFamily | userId, revokedAt, expiresAt | index(userId); family row is rotation/revocation lock |
| RefreshToken | familyId, secretHash, parentId, consumedAt, expiresAt, retryKeyHash, encryptedSuccessor, retryUntil | UNIQUE(secretHash), UNIQUE(parentId); one successor per parent; recovery ciphertext short-lived and key stored outside DB |
| Booking | customerId, serviceDate, state, version, baseMinor, addonMinor, discountMinor, totalMinor, currency, voucherId?, capacityAllocated, allocationReleasedAt?, cancellationReason | CHECK nonnegative and total=base+addon-discount; allowed states; index(customerId, createdAt), index(state, serviceDate); immutable quoted price after checkout intent |
| DailyCapacity | serviceDate PK, limit, allocated | CHECK 0 <= allocated AND allocated <= limit; default example limit 50 |
| Voucher | codeNormalized, kind, fixedMinor?, basisPoints?, currency?, maxUses, perUserLimit, expiresAt, enabled | UNIQUE(codeNormalized); checks on limits, discount shape and 0..10000 basis points; indexed redemption counts under voucher row lock enforce limits |
| VoucherRedemption | voucherId, userId, bookingId, status, reservedAt, releasedAt? | UNIQUE(bookingId); status Reserved/Consumed/Released; indexes(voucherId, userId, status) |
| PaymentAttempt | bookingId, sequence, requestKey, requestHash, gatewayKey, checkoutId?, status, amountMinor, currency, nextQueryAt | UNIQUE(bookingId, sequence), UNIQUE(bookingId, requestKey), UNIQUE(gatewayKey), UNIQUE(checkoutId) when present; partial UNIQUE(bookingId) WHERE status IN (Creating, Pending, Unknown, Succeeded); terminal failure only after authoritative no-charge result |
| CapturedPayment | bookingId, attemptId, gatewayTransactionId, amountMinor, currency, capturedAt | UNIQUE(bookingId), UNIQUE(attemptId), UNIQUE(gatewayTransactionId); positive captured amount; immutable ledger |
| WebhookInbox | provider, eventId, payloadHash, protectedPayload, status, attempts, nextAttemptAt, receivedAt | UNIQUE(provider, eventId); due-work index; event ID reused with a changed hash alerts/quarantines |
| RefundRequest | capturedPaymentId, gatewayKey, reason, status, gatewayRefundId?, nextAttemptAt | UNIQUE(capturedPaymentId), UNIQUE(gatewayKey), UNIQUE(gatewayRefundId) when present; one full refund only in initial scope |
| PerformerOffer | bookingId, performerId, status, expiresAt, acceptedAt? | partial UNIQUE(bookingId) WHERE status IN (Pending, Accepted); partial UNIQUE(performerId) WHERE status IN (Pending, Accepted); index(status, expiresAt); Accepted remains active until job ends |
| Earning | bookingId, performerId, amountMinor, currency, status | UNIQUE(bookingId); nonnegative amount; status Available/Reserved/Paid; completion creates once in same transaction |
| PayoutBatch | requestKey, performerId, currency, weekStart, status, totalMinor, exportedAt?, bankReference? | UNIQUE(requestKey), UNIQUE(bankReference) when present; status Pending/Paid/Failed; immutable sealed item set; index(weekStart, performerId); multiple recovery batches allowed for a week |
| PayoutItem | batchId, earningId, amountMinor, currency, status | UNIQUE(batchId, earningId); partial UNIQUE(earningId) WHERE status IN (Pending, Paid); FK batch/earning; Failed permits a new claim |
| StoredFile | ownerId, bookingId, kind, objectKey, storageVersion?, status, size, checksum, approvedAt? | UNIQUE(objectKey); status PendingUpload/Uploaded/Approved/Rejected; index(bookingId, kind); immutable approved object version |
| DeviceToken | userId, token, locale, invalidatedAt? | UNIQUE(token); user FK; invalid tokens deactivated |
| OutboxMessage | type, aggregateId, dedupKey, channel, recipient, protectedPayload, status, attempts, availableAt, leaseUntil?, leaseOwner?, claimVersion | UNIQUE(dedupKey); index(status, availableAt); dead-letter status and bounded retries |
| JobRun | jobName, scheduledFor, status, heartbeatAt, attempts, leaseOwner, leaseUntil, claimVersion, progress/result JSON | UNIQUE(jobName, scheduledFor); due/status index; durable resumption |
| GatewayOperation / GatewayCharge / GatewayEvent (fake_gateway schema in same DB) | stable key, requestHash, result; checkout/booking reference, amount/currency; event ID and encrypted payload | UNIQUE operation key with payload binding; UNIQUE charged booking reference; UNIQUE checkout charge; refund idempotency key unique; persistent across restart |

**Cuts/folds:** ExternalIdentity and Performer into User; UserRole into one role field; StaffInvitation replaced by explicit verified-UID provisioning (no public role-grant API); AssignmentCursor replaced by a fixed JobRun mutex row; PriceRule replaced by configured base/date fees; BookingTransition replaced by current state/version/reason and correlated logs; CapacityAllocation into Booking; VoucherUserUsage and counters replaced by indexed redemption counts under Voucher lock; CheckoutRequest into PaymentAttempt/booking checkout identity; NotificationDelivery into one recipient/channel per OutboxMessage; ReconciliationRun/Mismatch into JobRun report/cursor; SecurityAudit removed. This gives up multi-role accounts, self-service invitations, price administration and complete historical audit, not money invariants. Logs are not an audit ledger. Keep the remaining tables for the five required concurrency tests plus minimum identity/files/voucher/notification requirements; five tests alone do not satisfy the assignment.

**Replacement protocols:** every voucher redemption insert/release takes the Voucher row lock, then counts Reserved/Consumed redemptions globally and per user in a READ COMMITTED transaction; no counter tables. Booking allocation flag and date counter change atomically under booking lock, with release flipping the flag once. Different checkout request keys reuse the booking's live attempt; same key with changed payload conflicts. Round-robin locks a fixed JobRun row for a short SQL-only transaction, then uses sequence fields on User; that fixed row is not expiring scheduled work. Establish compatible lock order across scheduler, accept, expiry and suspension. Reconciliation stores a bounded JSON report or private report artifact reference on JobRun. Pricing and performer fees come from configuration and are snapshotted on booking/earning. Outbox stores a separate durable message per recipient/channel instead of a delivery table.

Use enum/check constraints for finite states, FKs for ownership references, and indexes for all worker due-work queries. Snapshot payout amounts/currency from earnings under lock; never accept client monetary totals. Cross-row equality is enforced by the transaction that owns those rows, not by pretending a CHECK can query another table. CHECK constraints cannot enforce allowed historical state transitions; one application transition component plus concurrency predicates does that. Direct arbitrary SQL is outside the supported write path.

## 2. Six highest risks: mechanisms and proof

### 1 — Double payment and idempotent checkout

Failure: double taps create two checkouts; a timeout/crash causes a fresh charge after the first already succeeded.

Mechanism: lock booking, create intent once, enforce partial unique live/successful attempt and request-key uniqueness, commit before gateway call. Send the same persisted gateway key on every create retry. Unknown attempts remain blocking until authoritative resolution. Never create another attempt merely because a local checkout timer expired. A failed attempt becomes retryable only when the gateway guarantees it cannot later capture. Fake gateway persists unique operation keys and one charge per booking. CapturedPayment uniqueness prevents two local credits, but cannot by itself stop two external charges. Real provider support is a required contract, not an exactly-once claim about HTTP.

Proof: 200 concurrent checkout requests with same and different request keys produce one live attempt/checkout; repeated pay calls and gateway restart produce one charge. Kill after provider commit and before local save, restart and retry: one gateway charge, one captured ledger row, one paid transition/outbox event. Changed payload with same key returns conflict. A pending/unknown response never unlocks another attempt.

**Explain it out loud:** We save a payment attempt before calling the gateway and reuse its key on every retry. Database constraints allow only one unresolved or successful attempt, and the gateway must persist its own idempotency guarantee. If a call times out, we recover that attempt instead of risking another charge.

### 2 — Duplicate / out-of-order webhooks

Failure: repeated success creates multiple offers, a delayed failure reverses success, or forged/mismatched events mark a booking paid.

Mechanism: verify HMAC over raw wire bytes before AES-GCM authenticated decryption; bounded body size and defined nonce/key-id contract. Persist inbox with UNIQUE(provider,eventId), then acknowledge. Background consumer re-queries authoritative gateway status outside the DB transaction. In a transaction lock attempt/booking, enforce amount/currency/booking-reference match, insert unique captured ledger and outbox, mark inbox complete. Repeated semantic success with different event IDs is still harmless. Successful capture never regresses to Pending/Failed. Refund is a separate ledger workflow. Re-query failure leaves inbox retryable; redirect only reads status/requests the same verification path.

Proof: parallel duplicate deliveries, distinct event IDs for one charge, pending→success and success→pending/failure, invalid signature/tag, changed event payload, wrong amount/currency/reference, provider outage. Assert one capture, one allocation, one paid event and no state regression; rejected events cannot change money state. Crash before inbox acknowledgement and after processing commit must be replay-safe.

**Explain it out loud:** A webhook tells us to check the gateway rather than proving that money moved. We store each event once and commit a verified capture with booking state and outbox together. Duplicates do nothing, and old failure messages cannot undo a successful capture.

### 3 — Daily capacity and late payment for a full date

Failure: concurrent successes oversell 50 places; a captured customer is silently left without a booking or refund.

Mechanism: allocate only on verified capture, not on checkout. In the capture transaction lock booking and execute UPDATE DailyCapacity SET allocated=allocated+1 WHERE serviceDate=@date AND allocated<limit RETURNING serviceDate. Same-row PostgreSQL update serialization and CHECK bounds enforce the limit. Set the locked booking's capacityAllocated flag and Paid state/outbox in that transaction. Precreate date rows, or INSERT ON CONFLICT before conditional allocation. A full date records the capture, transitions PendingPayment→Cancelled with capacity reason, and atomically creates unique RefundRequest plus accountant alert outbox. Never transition to Paid or offer work on overflow. Cancelled bookings follow the same compensation path. Release only if the locked booking has capacityAllocated=true, flip it false and set allocationReleasedAt while decrementing in the same transaction; lock booking first to prevent double release. Do not release a slot for completed service; after acceptance date changes are out of scope.

Proof: 200 concurrently verified paid bookings for capacity 50 yield exactly 50 active allocations, allocated=50, 50 Paid bookings, and 150 cancelled/refund-pending captures. Process refunds and assert 150 single full refunds/alerts. Repeat with cancellations, duplicate callbacks and worker restarts; counter equals bookings with capacityAllocated=true and never exceeds limit. k6 exercises actual API/gateway paths with 200 concurrent clients; DB assertions after workers drain prove invariants.

**Explain it out loud:** The database increments a date counter only if it is below the limit. That update and the booking allocation commit together, so 200 requests cannot take more than 50 places. A verified payment for a full date is recorded and refunded without creating an offer.

### 4 — Performer accept vs expiry

Failure: performer accepts while expiry assigns someone else; two bookings use the same performer.

Mechanism: accept and expiry lock booking then offer; acceptance also locks performer to serialize suspension. Acceptance condition requires Pending and expiresAt > database clock_timestamp() evaluated after acquiring locks; expiry requires Pending and expiresAt <= that clock. All transitions and outbox effects commit together. Partial unique active offer indexes cover both booking and performer; keep Accepted active until completion/cancellation. Retry by the accepting performer returns the original accepted result; another performer conflicts. Round-robin uses a locked fixed JobRun mutex row and User sequence fields, eligible performer row locks and active-offer constraints; suspended performers excluded at offer creation and acceptance. Short scheduler transaction is acceptable for this scale.

Proof: two connections plus a barrier run accept and expiry across the deadline; assert exactly one terminal offer outcome, one consistent booking state and no second assignment. Repeat accept concurrently, race two schedulers, suspend vs accept, and race two bookings for one performer. No test depends only on random sleeps.

**Explain it out loud:** Acceptance and expiry lock the same rows and check database time. Only one can change a pending offer, and unique active-offer indexes stop double assignment. The performer who already accepted can retry and get the same result.

### 5 — Payout double-run

Failure: concurrent weekly batches claim the same earning; rerunning a failed/exported batch sends money twice.

Mechanism: completion creates UNIQUE(bookingId) earning. Batch transaction locks eligible earnings (FOR UPDATE SKIP LOCKED), changes Available→Reserved and inserts items. Partial unique pending/paid claim per earning prevents concurrent active claims. Same request key returns same batch. Pending→Paid or Pending→Failed is a guarded transition under batch lock; update earnings and item status atomically. A Paid claim never returns to the pool; Failed releases only its own reservations. Batch totals are sealed before export. Bank file export is deterministic for the same batch and does not mark it Paid. Record accountant confirmation/reference with a unique bank reference before marking Paid. No automatic resend of an uncertain bank transfer.

Proof: concurrent workers for the same week, retries of same key, concurrent Paid/Failed confirmation and restart after claim commit. Sum of active/paid claims per earning is at most one; Failed permits exactly one later claim, Paid never does. Re-export is byte-stable and never creates another batch. Actual bank exactly-once execution is outside a CSV-export assignment and must not be claimed.

**Explain it out loud:** We lock eligible earnings and allow at most one pending or paid payout claim per earning. A failed batch releases its claims atomically, while a paid claim never returns to the pool. Exporting again does not mean paying again, and uncertain bank results need confirmation.

### 6 — Refresh replay / Firebase identity confusion

Failure: a stolen rotated token remains valid; two retries create branches; matching an email grants staff privileges.

Mechanism: verify Firebase signature, issuer/project/audience, expiry and revocation through Admin SDK before lookup; unique(project,uid), never email. New Firebase identity gets Customer only. Staff role/account linking requires explicit verified-UID role provisioning, without email linking or a public role-grant endpoint. Lock RefreshFamily for refresh, check expiry/revocation, consume token and insert one unique successor atomically. Store only token hashes as searchable credentials. To recover a lost response, require the same high-entropy refresh request key within a proposed 15-second window, return the same successor from short-lived encrypted recovery material. Never mint a different successor for a used parent. Changed retry key or replay after grace revokes family in a committed transaction before returning unauthorized; do not throw and accidentally roll back revocation. Purge recovery ciphertext after grace. A thief with both original token and matching retry key inside grace cannot be distinguished: document this bounded risk. Existing access JWTs expire within 10 minutes; family revocation alone does not retroactively revoke stateless JWTs.

### Refresh-token alternatives (N = 15 seconds, database time)

| | A: encrypted-successor recovery (recommended) | B: new child on consumed-parent replay in grace |
|---|---|---|
| First use | Consume parent, create/hash one child and save encrypted recovery value plus hash of high-entropy retry request key in same family-locked transaction | Consume parent and create/hash child; fix consumedAt once |
| Replay within N | Matching retry key returns identical child; changed key revokes family. If child is no longer usable, reject recovery rather than minting a branch | Mint another child without revoking family. Literal policy allows multiple valid children; this is not response-idempotent |
| After N | Commit family revocation and reject all descendants | Same; revoke all branches including grandchildren |
| Schema/keys | UNIQUE(parentId); purge recovery ciphertext after N; encryption key outside DB | Hashes only; parentId must NOT be unique; all branches share family |
| Trade-off | Encryption/key management and client retry key, but one predictable successor | Simpler storage/client, but someone with a stolen consumed parent can mint valid children during N; those children remain usable after N until expiry/revocation. Latest-child-only would need extra invalidation/client-race policy, not silently assumed |
| Proof | Matching retries return same child, mismatched/late replay revokes, restart recovers same child, unusable successor is not replaced | Concurrent retries issue distinct children within N; consumedAt never slides; after-N replay revokes every branch |

**Recommend A (30-second explanation is the sixth spoken summary below):** the small encryption cost preserves one child per parent and is easier to defend than session branching. The mechanism above describes A; B is an alternative, not simultaneously implemented. A thief with both parent and matching retry key inside grace remains indistinguishable from legitimate retry. All children in either design expire no later than their family.

Proof: chosen grace policy and committed revocation, concurrent family locking and restart recovery; same email with different Firebase UIDs never shares roles/account, including an Admin email. Wrong project, expired and revoked Firebase tokens fail. Shared ingress IP/verified-account throttling avoids victim lockout by unverified email; test key separation and document multi-instance configuration, no per-process guarantee.

**Explain it out loud:** A refresh token is single-use, with a 15-second same-request retry returning the identical replacement from short-lived encrypted storage. A changed retry key or replay after grace revokes the whole family. Firebase identities use only project and UID, so matching email never grants staff access.

## 3. Failure matrix

| Injected failure | Recovery and required assertion |
|---|---|
| Crash after gateway charge, before response/local commit | Persisted intent/key and gateway query recover one capture; restart does not create another charge |
| Checkout/charge timeout, including provider accepted request | Return pending/202 with stable attempt ID; keep Unknown blocking; retry/query same key; never treat timeout as definitive failure |
| Webhook before redirect | Callback can complete Paid independently; redirect observes existing result and adds no side effects |
| Payment after cancellation | Save capture, retain cancellation, request one full refund and one accountant alert; Refunded only after confirmed refund |
| Payment for full date | Capture tracked, no allocation/offer, cancellation plus one refund; refund failure remains visible/retryable |
| Crash between capture processing and outbox publish | Capture, booking state and outbox commit together or all roll back; dispatcher resumes |
| Crash after external send but before acknowledgement | Retry may duplicate email/push; stable event IDs/provider dedup where available, no false exactly-once promise |
| Webhook provider re-query unavailable | Durable inbox stays pending, exponential backoff then visible dead letter; no Paid decision from callback alone |
| Crash during refund request | Same refund key reused; restart/reconciliation discovers provider outcome; no double refund |
| Worker dies holding locks | Connection termination releases transaction row locks; durable committed claims become recoverable after lease expiry; durable rows resume; no lost committed intent |
| Expired outbox lease while first sender runs | Row fencing prevents stale DB completion; external duplicate still possible unless provider deduplicates |
| Concurrent voucher use at last availability | Lock voucher then count active global/per-user redemptions and reserve in one READ COMMITTED transaction; one winner |
| Approved object overwrite using old upload URL | Exact unique object key, create-only write condition or immutable storage version; approved reads pin version; overwrite attempt cannot replace approved evidence |

Use process kill/restart or deterministic failpoints at transaction/provider boundaries, not just exceptions before work starts. Tests assert gateway ledger as well as DB state. Reconciliation compares immutable gateway transactions by a durable cursor stored on JobRun, reports missing/mismatched captures/refunds in its report, and schedules the same idempotent verification flow; nightly reports alone are too slow for unknown checkout recovery.

## 4. Seven core steps: one green commit per step

User amendment: step 2 was split into separately tested/committed 2a (domain) and 2b (identity). Step 1 verification also has its own requested commit.

Steps 1, 2a, 2b and 3 are complete locally; wait for permission before step 4. Latest verification is targeted, not a new full regression run. Every implementation step ends with passing relevant tests, design/AI log update and one commit; prior tests remain green. Deployment needs actual hosting access; report blockers rather than claiming deployment. Optional polish is outside this build.

1. **Deploy skeleton:** project boundaries, API/gateway live/ready health, managed PostgreSQL app/fake_gateway schemas and restricted roles, migration runner, non-root multi-stage Dockerfiles, CI build/tests/Testcontainers/vulnerability scan, dev-main staging/main production. HTTP/DB health tests and externally deployed health pass; baseline correlation logs, environment-name reference and expand/contract procedure. Commit: `chore: deploy tested service skeletons`.
2. **Domain and lean identity:** reduced schema, explicit roles/guards, booking version, pricing; verified Firebase exchange, one role/account, JWT/selected refresh design, shared ingress throttle and UID provisioning. No invitations UI or linking workflow. Unit rules plus PostgreSQL replay/identity/authorization tests pass. Commit: `feat: enforce domain and identity rules`.
3. **Checkout and capture:** persistent fake gateway, failure controls, stable keys/Unknown recovery, crypto/inbox, query verification, capture/state/outbox and durable claim/recovery worker. Double-payment, duplicate/out-of-order, crash/timeout and redirect tests pass. Keep public sale/capture routes disabled until step 4; tests exercise them via test composition without production auth bypass. Commit: `feat: recover and verify idempotent payments`.
4. **Capacity, vouchers and refunds:** conditional capacity/booking allocation flag, release/cancellation, overflow/late-payment full refunds and alerts, voucher counts under lock. 200-client capacity/voucher and cancellation/refund races pass before sale/capture is enabled. Commit: `feat: enforce capacity and refund late captures`.
5. **Assignment and lean files:** round-robin, suspension, active-offer constraints and deadline race; one Azure Blob adapter, short exact-path grants, owner checks and immutable approved versions. No upload UI/transcoding/pipeline. Offer-race and file authorization/immutability tests pass. Commit: `feat: assign performers and protect proof files`.
6. **Completion and payouts:** proof/completion guards, earnings, concurrent batch claims, terminal status handling, bank reference and deterministic CSV. Double-run, failed-claim recovery and repeat-export tests pass. Commit: `feat: claim each earning once for payout`.
7. **Lean delivery and operations:** one FCM/email adapter each, plain Arabic/English templates, recipient outbox/backoff/dead letters and invalid-token removal; nightly report, health/heartbeat, graceful shutdown and basic OpenTelemetry. Two-instance/restart tests, remaining failure matrix, full suite/load tests and deployed smoke pass; finish DESIGN/AI log/operator docs. Commit: `feat: finish delivery recovery and operational checks`.

Durable outbox/job correctness starts in step 3, not step 7. Early public mutations are authenticated or disabled. Optional XLSX, admin UI and trace dashboards are not core steps.

**Job locking choice:** advisory locks are unnecessary: hosted services, UNIQUE(jobName,scheduledFor) and FOR UPDATE SKIP LOCKED transactional claims suffice when each claim persists owner, leaseUntil and monotonically increasing claimVersion. Unique rows prevent duplicate schedules, row locking selects one owner, leases recover committed claims after crash, and conditional heartbeat/completion rejects stale owners; unique rows and SKIP LOCKED alone without persisted ownership are insufficient once the transaction commits. SQL-only work stays in short transactions; external work uses durable intent and provider idempotency because a lease cannot stop a paused old process from already sending a request. Hangfire/Quartz offer richer scheduling/operations but do not replace business invariants; a few explicit hosted jobs and one tested claim protocol are smaller to defend here, while advisory locks add connection-lifetime coordination without solving external exactly-once delivery. The fixed assignment mutex uses an ordinary short transaction row lock. Prove two-instance exclusion, lease recovery and stale-owner rejection; promise one valid owner, not universal exactly-once execution.

**Gateway boundary:** separate HTTP service, same PostgreSQL database in fake_gateway schema, restricted gateway credentials and separate migration ownership. Core never reads gateway tables or shares a transaction with it; outcomes are verified over HTTP, retaining realistic timeout/crash tests. Shared DB outage/resource coupling is the accepted trade-off for less deployment/setup work.

## 5. Scope

**Must have:** all explicit backend requirements: verified identity/roles and refresh protection, guarded state machine, pricing/vouchers, payment simulation and encrypted callbacks, concurrency-safe capacity/checkout/offers/payouts, refunds/reconciliation, outbox notifications, private evidence storage, CSV export, durable jobs, meaningful telemetry/health, Testcontainers/load proof, deployed API+gateway, CI/security scan, environment and AI/design documentation.

**Nice to have:** XLSX in addition to CSV, admin dashboard, rich OpenTelemetry dashboards, extra providers, elaborate scheduling/fairness, extensive UI and reporting.

**Cut first:** admin UI entirely, XLSX (CSV satisfies the stated CSV/XLSX choice), multiple provider integrations, fancy templates, dashboard polish, separate worker deployment. Keep basic traces and required Arabic/English templates. If time still runs short, explicitly declare incomplete backend features; never hide missing security/concurrency behind demo behavior. Reduce supported policies (one currency, full refunds only, one fixed price policy, one storage provider) rather than removing invariants. Never cut payment, capacity, offer, payout or refresh replay tests.

## 6. Decisions and traps to document

| Ambiguity / trap | Recommended decision | Trade-off |
|---|---|---|
| Wrong Expo instructions | Deleted docs/Agents.md as requested; root AGENTS.md now defines .NET/interview rules | Repository guidance matches assignment |
| Absolute no-double-charge promise | Require persistent gateway idempotency and single charge per booking reference; unknown attempts block | Reduced availability while provider outcome is uncertain; DB alone cannot guarantee external effects |
| Slot reservation timing | Allocate only on verified payment; full date triggers automatic full refund and accountant alert | Simpler durable model; customers can temporarily pay for an unavailable date; expose that policy clearly |
| Booking exceptional transitions and roles unspecified | Customer owns Draft/checkout/cancel before work starts; accepted cancellation requires Supervisor. Assigned performer starts/submits proof, Supervisor completes/resolves disputes, system alone applies paid/refunded. Admin overrides still use guards and correlated reason logs | More explicit policy than spec; role never bypasses money constraints. Freeze full matrix before handlers |
| Dispute after payout | Permit dispute but flag paid earning for accountant/manual recovery; do not silently reverse exported/paid money | No automated clawback in initial scope; completed earning on open dispute excluded from new batches |
| Earnings formula / payout confirmation undefined | Snapshot a configured performer fee at completion; Accountant confirms bank result with reference; CSV export does not execute transfer | Requires agreed fee configuration and honest separation of file export from real payment |
| Failed payout status and retry | Failed is terminal for that batch; release earnings to a new batch. Do not mark Failed for an unknown bank result | Prevents accidental duplicate transfer but requires manual resolution of uncertainty |
| Refresh grace vs hash-only storage | Hash credentials; retain encrypted successor only for 15-second same-request replay, then purge | Strict no-recoverable-token policy would require deterministic derivation or no lost-response recovery; encrypted material is an explicit exception |
| Stateless access tokens and family revocation | 10-minute JWTs, revoke refresh family immediately; document residual JWT lifetime | Immediate access revocation needs DB/session checks on each request and is extra complexity |
| Firebase email matches staff account | Never auto-link; explicit privileged verified-UID provisioning | Staff onboarding needs one deliberate administrative step |
| Lockout abuse | Throttle by IP and verified account, avoid lockout keyed by submitted email; do not rely on per-process counters across instances | Shared limiter cost; failures without verified identity cannot safely count against a claimed victim |
| Voucher timing, rounding, expiry | Reserve atomically at checkout, validate expiry then; floor percentage discount in minor units, clamp discount to subtotal, consume on eligible Paid; release only on definitive no-charge/cancellation/refund policy | Unknown checkout retains reservation; add recovery, never release merely on timeout. Successful completed use stays consumed; full cancellation refund restores use once |
| Free booking after voucher | Initially reject zero-total checkout with clear validation and document minimum payable total | Avoids fake zero-value charge; supporting fully free bookings needs a separate explicit transition |
| Booking service date and capacity day | Business timezone Asia/Riyadh, UTC instants, immutable service date after checkout | Reschedule requires cancel/refund and new booking; no hidden cross-date counter movement |
| One active job per performer | Pending offer or accepted unfinished booking occupies performer, globally | Conservative throughput; no per-date scheduling unless requested |
| Currency and refunds | SAR only initially, halalas, full refunds only; refund ledger separate from capture | Partial refunds/multiple currencies deferred and disclosed |
| Approved-file overwrite | Azure private blob adapter with unique key/create-only upload and immutable version binding; verify actual SAS permissions in storage tests | A signed URL alone is not a sufficient no-overwrite guarantee |
| Outbox exactly once and job locking language | Transactional intent is atomic; processing is at least once with stable keys/constraints, row claims, leases and fencing | Email/push may duplicate after ambiguous delivery; no truthful universal exactly-once promise |
| Export format and broken PDF line ordering | Interpret payout transitions as Pending→Paid or Failed; CSV first; staging dev-main and production main | Document interpretation because source text wraps awkwardly |
| Hosting / zero-downtime migrations | Select one supported managed PostgreSQL host; expand schema before app rollout, contract in a later release, no destructive startup migrations | Requires deployment credentials and provider compatibility checks during step 1; no deployment claimed in this planning task |

No live provider/API/version claims have been verified during this repository-only planning task. Validate current .NET/provider configuration from official documentation when implementing, and record any resulting plan changes. No implementation beyond step 1 proceeds without further authorization.

Step-3 implementation note: durable immediate JobRun schedules use a name containing kind/entity ID with a canonical scheduled slot; AvailableAt controls retries. Inbox/outbox processing and gateway delivery have leases/fencing. Core production routes/worker activation remain withheld until step 4. Follow-up publication uses a private receiver contract; assignment/email/push implementations remain later scope. See DEMO_PREP.md for plain-language presentation.
