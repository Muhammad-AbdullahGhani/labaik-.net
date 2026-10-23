# Demo preparation - plain-language notes

Keep this beside the code. Steps 4-6 are committed and lean step 7 has passed its final local regression: 823 tests passed, none failed or skipped. Public mutations require authentication; callbacks require valid cryptography. See VERIFICATION.md for exact evidence and external limits.

## My opening (about 30 seconds)

"Labaik Core is a booking backend for an Umrah-by-proxy service. I focused first on protecting money and data when requests overlap, services restart, or a response gets lost. The API and fake gateway are separate HTTP services, using separate PostgreSQL schemas. I use database rules and transactions so the same rules hold when there is more than one server."

## Suggested presentation order (about 8 minutes)

1. Explain the product and the failure problem in one sentence each.
2. Show the four source layers and separate fake gateway; avoid reading every file.
3. Show the booking state diagram and one allowed/one denied transition.
4. Show integer pricing and one rounding example.
5. Explain identity using project plus Firebase UID; email is not an account key.
6. Show the verified new payment tests: double taps, a lost gateway response, repeated/old callbacks, then a worker restart.
7. Show the 200-user capacity/voucher checks, offer deadline race, private file protections and payout double-run.
8. Finish with the honest limits: no hosted service, real charge, bank transfer or live provider delivery was claimed.

## What the layers mean

- Domain: business rules without a database or HTTP dependency.
- Application: contracts for things outside the business rules, such as token verification and the gateway.
- Infrastructure: PostgreSQL, provider clients and durable workers.
- Api: HTTP endpoints and service wiring.
- FakeGateway: a separate simulation service. Core never reads its tables or shares its transactions.

## Short explanations I can say aloud

**State machine:** "A booking can only move along listed paths, and each path lists the roles allowed to use it. A failed move returns a reason instead of changing half of the booking. Ownership and real payment evidence still need application checks; a role alone is not proof."

**Money:** "I store halalas as whole numbers. Percentage discounts round down, fixed discounts stop at the subtotal, and arithmetic overflow is an error. I reject a free checkout because this version has no separate free-booking flow."

**Optimistic concurrency:** "Each booking has a version. An update must match the version it read. If another request saved first, the stale update fails instead of overwriting the winner."

**Identity:** "Firebase verifies the external identity. I look up project and UID, never email, then issue my own ten-minute access token. A new identity is always a Customer; staff access comes from a separate operator command that verifies the exact UID."

**Refresh tokens:** "A refresh token is single-use. For 15 seconds, the same retry key gets the same replacement from short-lived encrypted storage. A different key or a late replay revokes the family; existing access tokens can still last until their ten-minute expiry."

**Checkout idempotency:** "I save the payment attempt before calling the gateway. Every retry uses the same saved gateway key. A timeout means I do not know the outcome, so I recover that attempt instead of creating another charge."

**Webhook:** "A callback tells me to check the gateway; it does not prove payment. I verify its signature, decrypt it and store it once. I then ask the gateway for the current result and check the booking, amount and currency."

**Outbox:** "Paid state and the follow-up message are saved in one database transaction. A worker sends the message later. Delivery can repeat after a lost acknowledgement, so the receiver needs the stable message ID to deduplicate."

**Worker lease:** "A worker claims a row for a short time. If it dies, another worker can recover the row after the lease expires. The old worker must still prove ownership before saving, and external requests need idempotency because a lease cannot unsend a request."

## Problems encountered and honest fixes

- Docker initially could not start. I reported the blocked database test instead of claiming success.
- When Docker worked, a Testcontainers file mapping created a directory where the bootstrap script should be. Explicit file-path mapping fixed it; the real database test then passed.
- EF runtime and design tools initially chose different patch versions. Explicitly aligning them fixed the build.
- A constraint test initially changed unrelated test bookings. Binding the test's own booking ID fixed the test without weakening the constraint.
- Firebase's current credential API rejected a deprecated loader at build time. I used the typed service-account credential factory.
- Firebase SDK allows expiry clock skew. After SDK verification, I explicitly reject a token whose actual expiry has passed before issuing a new local session.

## Questions I expect

**Why PostgreSQL guarantees instead of a lock in C#?** A C# lock protects only one process. Database constraints and transactions coordinate every instance that writes to the database.

**Can your database prevent a real provider charging twice?** No. The provider must persist and honor the idempotency key too. My fake gateway models that contract; a real integration must verify it.

**Is delivery exactly once?** No. Local state and durable intent commit atomically; network delivery is at least once. Stable IDs and provider/receiver deduplication handle repeats.

**Why not mark timeout as failed?** The provider may have accepted the request before the response was lost. Calling it failed and opening another attempt risks two charges.

**Why a shared database for the fake gateway?** It reduces setup cost. Separate schema credentials keep the boundaries clear, but both services share database outage risk; this is a simulation trade-off.

**Why no Hangfire or Quartz yet?** There are only a few job types. PostgreSQL claim rows, leases and retry state are enough here, and a scheduler framework would not replace payment idempotency or database constraints.

## What I must not claim

- No public hosted deployment has been verified. Real Firebase token exchange against the local API is now user-verified; that does not verify FCM delivery.
- No real bank transfer, real HyperPay charge or actual customer card processing exists.
- Checkout and signed callbacks are enabled with capacity/refund protection; this remains a fake gateway, not real HyperPay.
- Local tests cover capacity, automatic full refunds, assignment, completion, payout claims, private grants and notification routing. Live provider delivery and bank acceptance remain unverified.
- The final full regression passed, including NBomber capacity and both voucher limits at 200 users. Use VERIFICATION.md for current totals.

## Commands and evidence

See VERIFICATION.md for observed commands/results, DESIGN.md for decisions, and AI_LOG.md for AI assistance. Use test-generated data; never display environment files, service-account JSON, API keys or tokens during the demo. The interview practice section below gives likely questions and follow-ups.

## Step 3 problems I can explain

**First new-suite failure:** "The gateway callback test sent to the root URL instead of the webhook path. I corrected the test client's base address. The signed callback and duplicate-credit test then passed; I did not weaken the assertions."

**Framework change:** "The compiler rejected deprecated test-host constructors because warnings are errors. I switched the test hosts to the supported HostBuilder API instead of turning off that protection."

**Stale network result:** "A database row lock does not make a response from earlier in time fresh. I keep terminal payment states final, so a slow Pending query cannot reopen an attempt already confirmed Failed or undo a successful capture. There is a deterministic race test for this."

**How I model crashes:** "The fake gateway can commit a checkout or charge and then return an error or lose the response. I restart the HTTP host against the same PostgreSQL data and retry the same key. This tests the failure boundary; I do not claim I killed an OS process in that test."

**Atomicity test:** "I make PostgreSQL reject the outbox insert on purpose. Then I check that the capture, Paid state and inbox acknowledgement all rolled back too. Removing that fault lets the same durable job retry successfully."

## My three-minute payment demonstration

1. **Explain the problem:** "Imagine I tap Pay twice and the first response never arrives. Retrying with a new charge would be dangerous."
2. **Show PaymentCheckout.cs:** point to the booking row lock, saved attempt/key and the commit before the HTTP call. Say: "The intent survives even if this process stops now."
3. **Show the gateway migration/service:** point to the unique key and unique charge per booking. Say: "The provider side must protect retries too; my database alone cannot protect an outside charge."
4. **Run a focused failure test:** use the commands below. Show the test's three phases: provider committed, response lost, restart/retry recovered one charge.
5. **Show the callback-before-response test:** explain that the callback can finish first. A later redirect or create response must not move a successful payment back to Pending.
6. **Show the transaction in PaymentPump.cs:** identify capture, booking state and outbox written together. Then show the forced-write-failure test.
7. **Finish honestly:** "This stage is tested internally. Public checkout is still off while I add capacity allocation and automatic refunds. Email, push and performer work will consume the durable events later."

## Focused demo commands (Docker must be running)

Build once:

```powershell
dotnet build --configuration Release
```

Show one lost-response/restart proof (runs only this NEW test):

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --no-build --filter FullyQualifiedName~PaymentIntegrationTests.Crash_after_charge_commit --logger "console;verbosity=normal"
```

Show the other two failure boundaries individually:

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --no-build --filter FullyQualifiedName~PaymentIntegrationTests.Webhook_before_create_response --logger "console;verbosity=normal"
dotnet test tests/Labaik.IntegrationTests --configuration Release --no-build --filter FullyQualifiedName~PaymentIntegrationTests.Paid_capture_and_outbox_roll_back --logger "console;verbosity=normal"
```

The full NEW payment suite, if there is time (28 cases; not the older domain/identity suite):

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter FullyQualifiedName~PaymentIntegrationTests
```

Observed final targeted output:

```text
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28, Duration: 10 s - Labaik.IntegrationTests.dll (net10.0)
```

Tests create an isolated PostgreSQL container with generated data and remove it afterward. The Core/gateway services talk over HTTP TestServer clients; each uses its own schema credential. Read the assertions for local attempt count, gateway charge count, capture count and paid-event count. Those are the facts to defend, rather than just saying "the test is green."

## Payment questions and easy answers

**Why sign and encrypt?** "The signature authenticates the raw message before I process it. Authenticated encryption protects the contents and detects tampering too. I use different keys and keep them out of the repository."

**What if the callback arrives twice with different event IDs?** "Inbox uniqueness catches exact event repeats. Capture and outbox uniqueness plus the booking transaction also catch repeated success under a different event ID."

**What if a paid booking was cancelled?** "I retain the capture and cancellation, and create durable refund work and an accountant alert. The refund worker reuses one provider key until a confirmed result is recorded. Requested is not the same as refunded."

**What if the worker dies after sending?** "The receiver might see the same message again. I send the same message ID every time so a durable receiver can recognise the duplicate. The test receiver stores one receipt even when it receives two deliveries."

**Why did payment routes stay disabled until step 4?** "The payment flow needed capacity and refund safeguards first. Those safeguards now exist, so checkout is authenticated and callbacks require a valid signature."

**How do retries stop?** "Provider failures back off and eventually leave a visible dead-letter job. A normal still-pending payment is not counted as a provider error, and it never causes me to open a new charge attempt."

**Which files should I open?** PaymentCheckout.cs for intent/idempotency, GatewayService.cs for the provider contract, WebhookCodec.cs for message protection, PaymentPump.cs for the commit, WorkQueue.cs for claims, and PaymentIntegrationTests.cs for proof. Generated migrations/snapshots record the EF schema; explain the named checks/indexes rather than reading generated boilerplate aloud.

## Step 4 demo update

"The date has a counter that the database can increment only while it is below the limit. Capture and booking allocation commit together. A payment arriving for a full date is recorded and refunded, rather than silently accepted."

Actual focused result: 200 paid attempts -> 50 fulfilled booking slots and 150 confirmed full refunds. Five new tests passed, including voucher limits and cancellation races. Public booking/callback routes are now protected and available; automatic processing needs configured gateway keys and worker activation. Earlier disabled-route notes describe historical step 3, not the current stage.
## Explain assignment and private files
“Only one short database transaction chooses a performer at a time. Both accepting and expiring an offer take that same lock and check the database clock. Unique indexes also stop a booking or performer having two active offers.”
“Files live in a private container. Upload links last five minutes, allow creating one exact object and cannot overwrite it. Before a read link is issued we check the user and booking; approval freezes the saved object identity.”
Demo: show the accept/expiry test, then the file test rejecting another user's read, a changed upload path and an overwrite. Azure was tested locally with Azurite, not deployed to a cloud account. File metadata validation is not malware scanning.
## Explain payouts in 30 seconds
“A completed booking creates one earning after a supervisor approves proof. A payout batch claims earnings in a database transaction, and a unique index prevents another live batch claiming the same earning. Exporting a CSV does not mark money paid: an accountant records the confirmed bank result, and only a confirmed failure returns earnings to the pool.”
Demo: run PayoutTests, point to UX_Items_ClaimedEarning and the unique booking earning index, then show that repeat CSV export leaves Pending. Never retry an uncertain bank transfer as a new batch. The CSV is a generic bank instruction format; a real bank must approve its format before use. Saudi IBANs come from trusted environment configuration, not customer input, and are snapshotted on first export.


## Finish the demo: notifications, recovery and limits

?Payment confirmation saves follow-up work in the same transaction. A worker creates assignment and recipient jobs later. Each channel retries independently, and a bad device token is removed; a lost delivery acknowledgement may still cause a repeat push.?
?The nightly comparison reports differences between the gateway and our database. It does not guess that money moved or silently fix a ledger. The operations endpoint shows reports, payment alerts and jobs that exhausted their retries.?
?Each worker writes a heartbeat. Readiness checks the database, configured private storage and this instance's worker. Turning off the process does not erase queued work: another process can recover an expired claim.?

Suggested five-minute version:

1. Product, layers and separate gateway: 30 seconds.
2. Payment uncertainty/idempotency and webhook re-query: 60 seconds.
3. Capacity test: 200 paid customers, only 50 slots, 150 refunds: 45 seconds.
4. Offer deadline race and payout double-run: 60 seconds.
5. Private file grant and Firebase UID separation: 45 seconds.
6. Actual test evidence and honest limitations: 40 seconds.

Run focused demos with `dotnet test tests/Labaik.IntegrationTests --configuration Release --filter FullyQualifiedName~CapacityRefundTests`, then AssignmentTests, PayoutTests, PrivateFileTests or LoadTests as needed. Do not run all suites live in a short interview: show the saved verification results and rerun one relevant proof. A concise load report is checked into load/results; it is a local correctness check, not a production throughput promise.

What I cut: admin UI, XLSX, elaborate templates, multiple providers, trace dashboards and separate worker deployment. What still needs real access: hosted services/GHCR, actual Firebase login/push, Azure cloud storage, Resend recipient delivery and bank-specific CSV acceptance. Email destinations are currently operator configuration, not self-service email linking. There is no automated bank transfer, partial refund, dispute clawback or malware scanning.


A useful failure story: the local upgrade check caught an already-applied migration being regenerated under a different ID. The migration task failed and blocked rollout. I kept the old migration and added a separate additive migration, then tested both a fresh database and the retained database. The lesson is that an applied migration is history: do not rewrite it, even if a fresh-database test passes.

Exact final test wording: ?The full Release run passed 823 tests. I then changed stale-notification handling and migration history, so I ran the affected 15 tests again; all passed. I did not claim a second full regression after those fixes.?


Final local stack proof: both services answered live/ready with 200 and ran as non-root; the migration upgrade succeeded against the retained database. I stopped the stack cleanly afterward. Restart it with docker compose up --build --wait for the demo; use the focused test commands if live Firebase credentials are unavailable.

## Interview practice: what to learn first

These are likely questions, not a prediction of the interview. Practise answering without reading. For each answer, give the problem, the mechanism, the test and one limitation. Start with questions 1-12; then cover identity and the .NET details.

### 1. What problem does this project solve?

Say: "It manages bookings, payment confirmation, performer work and payout records for an Umrah-by-proxy service. The hard part is keeping money and booking state correct when requests overlap or a service fails. I prioritised those failure cases before optional UI."

Follow-up: **What is your most important guarantee?** A timeout cannot by itself open another charge attempt, and verified captures cannot allocate more than the date limit. External charge protection also depends on the gateway honoring persistent idempotency.

### 2. What is idempotency? Is it just rejecting duplicates?

Say: "Repeating the same operation has the same business effect. If a checkout response is lost, the retry recovers the existing attempt instead of creating another charge. The same key with changed input conflicts."

Follow-up: **What if the customer uses a different key?** The booking still has only one unresolved or successful payment attempt, protected by locking and a partial unique index. A new client key does not bypass that booking rule. Open PaymentCheckout.cs and PaymentModel.cs.

### 3. What happens if the gateway charges and your API crashes?

Say: "We already saved the attempt and gateway key before calling it. After restart, recovery queries or retries that same operation. It never assumes that a lost response means no money moved."

Follow-up: **Can you promise exactly one real charge?** Only with the provider's persistent idempotency contract. A unique local capture row stops double credit locally; it cannot undo an unrelated external charge. Demonstrate the response-loss/restart tests, and say that these are deterministic failure simulations rather than literal OS process kills.

### 4. How do 200 requests avoid taking more than 50 places?

Say: "Each verified capture tries one conditional database update: increment the date counter only when allocated is below the limit. That update and the booking allocation commit together. A full date keeps the capture record and creates refund work instead of accepting the booking."

Follow-up: **Why not read the count and then add one?** Two requests could both read 49 and both decide a slot is free. The conditional update makes the decision and write one database operation. A CHECK constraint also rejects out-of-range counters. Show BookingMoney.cs and CapacityRefundTests.

### 5. What is a race condition? Why not use lock in C#?

Say: "A race happens when the result depends on overlapping operations. A C# lock protects only one process, while two API instances can both write to PostgreSQL. Database row locks, conditional updates and constraints coordinate those instances."

Follow-up: **Does locking everything solve it?** It can destroy throughput and create deadlocks. Keep locks short, take them in a consistent order and keep network calls outside the transaction. Our shared assignment/payout mutex is deliberately conservative for this small system, and is a scaling trade-off.

### 6. What is a transaction? Explain ACID in this project.

Say: "A transaction makes a group of database changes commit together or roll back together. For payment confirmation, capture, booking state, capacity, voucher use and outbox intent belong together. Otherwise we could have a paid booking without its follow-up work."

- Atomicity: all those writes commit or none do.
- Consistency: valid writes preserve rules such as nonnegative money and bounded capacity.
- Isolation: overlapping transactions interact according to PostgreSQL's isolation and locking rules; isolation does not mean every transaction is automatically serializable.
- Durability: committed data survives an application restart, subject to the database's durability configuration and storage guarantees.

Follow-up: **Does this include the gateway HTTP call?** No. It is a separate service with its own transaction. Persisted intent, stable keys and recovery bridge the two systems. Show the test that deliberately fails the outbox insert and checks rollback.

### 7. Optimistic concurrency versus row locking: why use both?

Say: "A version check rejects a booking update based on an old version. A row lock makes competing transactions wait while a short decision is made. We use versions for stale booking writes and locks when a decision involves related rows."

Follow-up: **Does EF add this automatically?** We configure Version as a concurrency token and increment it on tracked booking updates. EF includes the original version in the UPDATE predicate; no matching row raises a concurrency exception. Raw SQL booking updates must handle version checks/increments explicitly. Open AppDbContext.cs and the stale-version test.

### 8. Why are both an inbox and an outbox needed?

Say: "The inbox stores incoming gateway events once so processing can resume after a crash. The outbox stores outgoing work with the booking change, so follow-up work is not lost. They protect different sides of the workflow."

Follow-up: **Can a notification repeat?** Yes: sending can succeed before its acknowledgement is saved. We reuse a stable message ID; email provider deduplication helps within its supported contract, while push may repeat. At-least-once processing is not universal exactly-once delivery.

### 9. What if webhooks are duplicated, old or forged?

Say: "We authenticate the raw encrypted body, decrypt it and deduplicate the event. Then we ask the gateway for its current result and check booking reference, amount and currency. We never let an old Pending or Failed observation undo a successful capture."

Follow-up: **What if two different event IDs describe the same payment?** The capture and business-event uniqueness rules still prevent double credit. Inbox deduplication alone would not be enough. A browser redirect only reads status; it cannot mark Paid. Open WebhookCodec.cs and PaymentPump.cs.

### 10. How can accepting an offer and expiring it happen safely together?

Say: "Both paths use the same short database mutex and check the database clock. An offer must still be pending and before its deadline to accept. Active-offer unique indexes also prevent a booking or performer having two active assignments."

Follow-up: **Why database time?** API instances may disagree slightly about the clock. One time source makes the deadline decision consistent. Acceptance is repeatable for the same accepted offer and performer. Show Assignment.cs and the concurrent accept/expiry test.

### 11. How do you prevent paying an earning twice?

Say: "Completion creates one earning per booking. A batch reserves earnings in a transaction, and a partial unique index allows only one Pending or Paid payout item for an earning. Confirmed Failed releases the earning; an unknown bank outcome stays Pending."

Follow-up: **Does exporting the CSV pay the performer?** No. It is an instruction file. An accountant records a confirmed bank result with a unique reference; a repeated export does not change Pending to Paid. This is local payout accounting, not proof of exactly-once real bank execution. Show Payouts.cs, PayoutModel.cs and PayoutTests.

### 12. How do workers recover after a crash? What does SKIP LOCKED mean?

Say: "A worker claims a persisted job with an owner, expiry time and increasing claim version. SKIP LOCKED lets another worker pick different work instead of waiting on a row being claimed. After a crashed worker's lease expires, the job can be claimed again."

Follow-up: **What if the old worker wakes up?** It must match the current owner/version and a valid lease before committing effects. This is fencing. It cannot unsend an external request, so provider idempotency is still needed. Failures back off and eventually become visible dead letters; operators must investigate outcomes before retrying. Open WorkQueue.cs.

### 13. Authentication versus authorization: what is the difference?

Say: "Authentication establishes who you are. Authorization decides whether that identity may perform this action on this booking or file. A valid JWT does not mean you can read someone else's passport."

Follow-up: **Why verify a Firebase token instead of just decoding it?** Decoding only reads claims. The Admin SDK verifies the signature, expected project, expiry and revocation; we also reject an already expired token after verification. Tests use the real SDK with controlled HTTP responses; the configured real Firebase/local API exchange was subsequently verified by the user.

### 14. Why identify by Firebase project and UID instead of email?

Say: "Email is not our account-linking or role-grant key. Two Firebase UIDs with the same email stay separate local users. A new identity gets Customer only; privileged roles require explicit provisioning of the verified UID."

Follow-up: **What stops someone using an Admin's email?** Matching the email has no effect. The unique identity key is project plus UID, and roles are loaded from the local user. Explain the identity-separation test.

### 15. What is a JWT? Is it encrypted? Why ten minutes?

Say: "Our JWT is a signed, short-lived access credential. Its claims are readable; the signature protects their integrity, not their confidentiality. Ten minutes limits the lifetime of a stolen token, while refresh tokens allow the session to continue."

Follow-up: **Does refresh-family revocation immediately kill access JWTs?** No. Already issued access tokens can remain valid until expiry; some business endpoints also check current database user status/role. Do not claim a universal immediate access-token revocation mechanism. Never put secrets in JWT claims.

### 16. Explain refresh-token rotation and the grace window.

Say: "Each refresh token is consumed once, under a family row lock. The same parent and retry key recover the identical encrypted successor for 15 seconds. A changed key or replay after grace commits family revocation before returning an error."

Follow-up: **Why hashes plus encryption?** Hashes verify random refresh credentials without storing them directly. The short-lived encrypted successor is a deliberate exception for lost-response recovery, using a separate key outside the database. A slow legitimate retry outside grace must sign in again; that is the security/usability trade-off. Open IdentityService.cs and TokenCodec.cs.

### 17. How do rate limits avoid locking out a victim?

Say: "Unverified callers consume an IP budget, not the budget of an email they type. The account budget is selected only after a verified identity or stored refresh credential establishes the account. The counters are shared in PostgreSQL, so another API instance does not reset the limit."

Follow-up: **What changes behind a reverse proxy?** Trusted proxy configuration must be set for the actual host. Blindly trusting X-Forwarded-For would let callers invent IPs; currently the socket address is used, which can share one ingress budget.

### 18. Why integer money? How do discounts round?

Say: "Money uses SAR halalas in a long, with checked arithmetic. Percentage discounts round down to a whole minor unit; fixed discounts cannot exceed the subtotal. The server calculates the quote, and zero-total checkout is rejected in this version."

Example: 1,001 halalas at 10 percent gives a discount of 100 halalas and a total of 901. Follow-up: **Why not decimal?** Decimal can model money, but integer minor units make this single-currency policy and rounding explicit. Binary float/double are unsuitable for exact currency arithmetic here.

### 19. How can a voucher's maximum uses stay correct under concurrency?

Say: "Reservation takes the voucher row lock and counts Reserved and Consumed redemptions globally and per user. Every reservation/release follows that locking protocol. A slow or unknown payment retains its reservation until a definitive outcome, rather than releasing it on timeout."

Follow-up: **Is COUNT by itself safe?** No: without the shared row lock, concurrent requests could both see a free use. Expiry is checked when reserving at checkout; the quoted discount does not give unlimited permission to redeem later. Show the 200-user voucher tests.

### 20. Which database tools protect which rules?

| Tool | Meaning | Example here |
|---|---|---|
| Primary key | Identifies one row | Booking ID |
| Foreign key | Requires a referenced row | Earning must reference a booking |
| CHECK | Validates values within a row | Allocated cannot exceed Limit |
| Unique index | Prevents duplicate indexed values | Firebase project/UID; capture per booking |
| Partial unique index | Uniqueness only for selected rows | One active offer or live payment attempt |
| Transaction/lock | Coordinates a multi-row decision | Voucher counting and reservation |

Follow-up: **Can a CHECK enforce the whole state machine?** No. A normal CHECK cannot compare the previous row state or coordinate arbitrary other rows. The application state-machine component, guarded writes and concurrency checks enforce supported transitions. Arbitrary direct SQL is outside that supported write path.

### 21. What are deadlocks and isolation levels?

Say: "A deadlock is a cycle: transaction A waits for a lock held by B while B waits for A. Consistent lock order and short transactions reduce that risk. PostgreSQL can abort a deadlocked transaction, so any retry must preserve the original operation keys."

Follow-up: **Why not make everything Serializable?** It can detect more conflicts but requires handling transaction retries, and cannot include a gateway's HTTP transaction. Our default PostgreSQL Read Committed transactions use explicit locks, conditional updates and uniqueness for the specific invariants. Do not claim automatic deadlock retries everywhere.

### 22. How are files private and approved proof protected?

Say: "The container is private. After ownership/role checks, the API grants access to one exact object for five minutes. Upload grants are create-only, without overwrite permission; stored ETag/version identity is checked when reading approved evidence."

Follow-up: **Does a signed upload link enforce file size or prove the content is safe?** No. Completion checks size and content-type metadata; metadata is not malware scanning or full file-content validation. The storage administrator remains trusted. Show the real Azurite path/ownership/overwrite test.

### 23. Why these architecture boundaries and interfaces?

Say: "Domain contains rules without HTTP or database dependencies. Application defines provider contracts. Infrastructure contains EF-backed orchestration, workers and provider adapters; Api handles HTTP and composition. The fake gateway has its own HTTP and database credential boundary."

Follow-up: **Is every use case in Application?** No. This lean version keeps transaction-heavy orchestration in Infrastructure rather than introducing an abstraction for every EF operation. That is an explicit compromise; pure rules stay in Domain and external services have interfaces for controlled tests. No generic repository or mediator was added without a concrete need.

### 24. What .NET concepts might they ask you to explain?

- Dependency injection: the host constructs services and supplies their dependencies; registration does not itself call the provider.
- Scoped lifetime: usually one service instance per HTTP request or explicitly created worker scope. DbContext must not be shared between concurrent operations.
- Singleton lifetime: one instance per application container; its mutable shared state would need care. It cannot replace a distributed database guarantee.
- async/await: lets a thread serve other work while I/O is pending; it does not make the database operation atomic or guarantee parallel execution.
- CancellationToken: requests cooperative cancellation. Cancelling a local HTTP wait does not prove the remote provider cancelled the charge.
- AsNoTracking: useful for read-only EF queries; fewer tracked objects. It is not a lock and does not make a read current forever.
- ExecuteSqlInterpolated: EF parameterizes interpolated values. Do not replace it with string-built SQL containing user input. Fixed FromSqlRaw statements here contain no interpolated user input.

Follow-up: **Why create a scope inside a worker?** A hosted worker lives a long time; each work iteration needs its own short-lived DbContext and scoped services.

### 25. What tests actually prove the design?

Say: "Unit tests prove pure state, role, pricing and voucher rules. Real PostgreSQL tests prove constraints, concurrent claims and rollback behavior. The load tests exercise production HTTP endpoints through TestServer with 200 users, then assert database invariants."

Follow-up: **Why not an in-memory database?** It would not reproduce PostgreSQL row locks, partial indexes or SQL CHECK behavior. **Do 823 passing tests prove production readiness?** No. That full run passed, then 15 affected tests passed after follow-up changes. Live provider acceptance, hosting, bank certification and real-network performance remain separate work. Read assertions and failure boundaries, not just the test count.

### 26. How do you deploy, monitor and recover safely?

Say: "Migrations run as separate tasks with schema-owner credentials. Runtime services have narrower credentials and never migrate implicitly. Readiness checks the database, configured storage and this instance's enabled worker heartbeat; liveness only says the process can respond."

Follow-up: **Can you rewrite an applied migration?** No: preserve it and add a new one. Explain the caught local upgrade failure. **How do mixed worker versions roll out?** Disable old workers, apply additive changes, roll compatible API replicas, then enable new workers; HTTP can retain durable work during the processing pause. A dead letter needs investigation, not a new payment key. Basic traces/logs omit personal data, secrets and signed URLs.

### 27. What would you improve, and how did you use AI?

Say: "I would first verify the real providers, bank format and hosting, then add audited operational procedures, file inspection and stronger large-volume job/report handling. The short global mutex is simple to defend here but would become a bottleneck at higher volume. I cut dashboard/XLSX polish before correctness."

Follow-up: **Did you write everything yourself?** "I used AI for proposals, implementation assistance and test scenarios, and recorded that in AI_LOG.md. I need to explain the invariants and inspect the relevant code myself; a green test or generated answer does not replace that responsibility." Never claim to understand a line you cannot explain. If unsure, say which assumption you would check and how you would test it.

## Rehearsal checklist before Firebase setup

1. Explain idempotency versus a transaction in your own words.
2. Draw: saved attempt -> gateway call -> verified capture -> atomic booking/outbox commit -> background work.
3. Explain why timeout means unknown, why a redirect cannot prove payment and why an outbox can deliver twice.
4. Explain the conditional capacity update and the refund path for the 51st booking.
5. Explain optimistic version checking versus a row lock, and why a C# lock is insufficient across servers.
6. Explain accept versus expiry, earning claims and why CSV export is not payment.
7. Explain Firebase identity versus local authorization, then refresh rotation and its grace trade-off.
8. Open one test for each claim and point to the final assertions. Finish by naming one honest limitation.

Aim for 20-40 seconds per main answer. Let the interviewer choose the deeper follow-up; do not try to recite this whole document.

## Live Firebase demo: current setup and presentation

Live login is now verified by the user's observed PowerShell result on 23 October 2026: Firebase sign-in succeeded, POST /auth/firebase issued a local session, and GET /auth/me returned a local userId and Customer role. No actual user identifier, email, password, API key or token is stored in these notes. This is local API verification against real Firebase, not a hosted deployment or verification of FCM delivery.

Show this short sequence without displaying token objects or credentials:

1. Show both services' healthy endpoints; say this proves service readiness, not successful Firebase login by itself.
2. Sign in to Firebase with the demo account; keep its ID token in a PowerShell variable.
3. Send that token to POST /auth/firebase; keep the returned local JWT/refresh token in a variable.
4. Call GET /auth/me with the local JWT. Show only userId and Customer role.
5. Call /auth/me without a token and show 401.
6. Explain: Firebase authenticates the identity; our API verifies it and assigns local permissions. Email does not link accounts, new identities cannot request Admin, and staff provisioning verifies the exact UID through a separate operator command.

Failure story: health was green while auth exchange returned 503 because local token keys were absent. The API failed closed instead of issuing an insecure session. We inspected configuration presence without printing secrets and added missing local keys without rotating database credentials.

Second failure and lesson: after adding local keys, exchange still failed. Safe logs showed that our adapter explicitly set the Firebase SDK HTTP factory to null. Earlier tests injected a fake factory, so they did not exercise production defaults. We preserved the SDK default and added a separate default-path test; all seven Firebase adapter tests passed. Say: "A test double can hide a production initialization bug. I added coverage for both the controlled test transport and the real default configuration path." The user then retried and observed successful /auth/me with Customer role.

## Repeatable Firebase interview walkthrough (about three minutes)

### Before sharing your screen

Firebase setup already completed: registered a project/Web app, enabled Email/Password, created a demo account and obtained the Admin service-account JSON. The Web API key identifies the project for client sign-in; the service-account private key authorizes the trusted backend. They are different credentials. Firebase project/UID identifies the user; the local database stores their role. No Firestore database or Firebase Hosting is needed for this walkthrough.

Open PowerShell in the repository. Run this preparation privately before sharing your screen. Supply the service-account file path without extra quote characters; keep that JSON outside the repository. Existing .env must already contain the independent local signing/recovery keys.

```powershell
Set-Location 'C:\Users\i222683AbdullahGhani\Desktop\TASK\labaik-core-dotnet'
$credentialPath = Read-Host 'Path to service-account JSON'
$firebaseCredential = Get-Content -LiteralPath $credentialPath -Raw | ConvertFrom-Json
$env:FIREBASE_PROJECT_ID = $firebaseCredential.project_id
$env:FIREBASE_CREDENTIALS_JSON = $firebaseCredential | ConvertTo-Json -Compress -Depth 10
docker compose up -d --wait

$firebaseApiKey = Read-Host 'Firebase Web API Key'
$demoEmail = Read-Host 'Demo account email'
$demoPassword = Read-Host 'Demo account password' -AsSecureString
Clear-Host
```

Keep this same window open. Environment variables belong to this shell session; a new shell must load them again before recreating the API. Clear-Host removes visible output, not command history; prompts keep credential values out of the commands typed into history. Never expand or display the credential/token variables.

### Show 1: services can respond

```powershell
Invoke-RestMethod 'http://localhost:8080/health/ready'
Invoke-RestMethod 'http://localhost:8081/health/ready'
```

Expected: Healthy twice. Say: "The API and fake gateway can reach their required configured dependencies. This does not by itself prove that a user can sign in."

### Show 2: sign in, exchange and call the protected endpoint

Paste only the code block, not PowerShell's PS or >> prompt characters. The final closing brace matters. This stops if either login stage fails, so an old session is not accidentally presented as a new success.

```powershell
$session = $null
$firebaseLogin = $null
$loginBody = @{
    email = $demoEmail
    password = ([System.Net.NetworkCredential]::new('', $demoPassword)).Password
    returnSecureToken = $true
} | ConvertTo-Json

try {
    $firebaseLogin = Invoke-RestMethod `
        -Method Post `
        -Uri "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=$firebaseApiKey" `
        -ContentType 'application/json' `
        -Body $loginBody `
        -ErrorAction Stop
    Write-Output '1. Firebase sign-in succeeded; ID token kept private.'

    $session = Invoke-RestMethod `
        -Method Post `
        -Uri 'http://localhost:8080/auth/firebase' `
        -ContentType 'application/json' `
        -Body (@{ idToken = $firebaseLogin.idToken } | ConvertTo-Json) `
        -ErrorAction Stop
    Write-Output '2. Local token exchange succeeded; tokens kept private.'

    Invoke-RestMethod `
        -Uri 'http://localhost:8080/auth/me' `
        -Headers @{ Authorization = "Bearer $($session.accessToken)" } `
        -ErrorAction Stop
}
finally {
    Remove-Variable loginBody, demoPassword -ErrorAction SilentlyContinue
}
```

Expected final result: a userId and role Customer. Use a dedicated demo account if displaying the identifier. Say: "Firebase checked the login credentials. My API verified the Firebase ID token through Admin SDK, including revocation, then found the user by project and UID. It issued its own ten-minute JWT, which this protected endpoint accepted. A new identity gets Customer only."

If repeating the demonstration, prompt for $demoPassword again; the finally block clears it. A Firebase ID token is not our API JWT. A service-account JSON file is neither of those user tokens.

### Show 3: no credential means no access

```powershell
try {
    $null = Invoke-RestMethod 'http://localhost:8080/auth/me' -ErrorAction Stop
    Write-Output 'Unexpected result: unauthenticated request succeeded.'
}
catch {
    if ($null -ne $_.Exception.Response) {
        Write-Output "Without a Bearer token: HTTP $([int]$_.Exception.Response.StatusCode)"
    }
    else {
        Write-Output 'Request could not reach the API; this is not an authorization proof.'
    }
}
```

Expected: HTTP 401. Say: "The same endpoint rejects a request without a valid access token." This is a live check to perform; the successful Customer request has already been observed. Role ownership, refresh replay, wrong-project/expired/revoked tokens and same-email identity separation have separate automated tests; this short login demo does not re-prove all of them.

### Questions to answer after the walkthrough

- Why not send the password to our API? The demo sends it to Firebase; our API accepts a Firebase ID token and does not manage the user's Firebase password.
- Why issue a local JWT? Our API controls its own audience, short lifetime, local roles and rotating session credentials.
- Why not let the client choose Admin? A client role request is not authority. Privileged roles require the separate verified-UID provisioning command.
- Does Customer output alone prove no email linking? No; show the project/UID uniqueness and identity-separation test for that stronger claim.
- Is push working too? This verified authentication only. FCM requires a real registered device and a separate delivery smoke test.
- Is this deployed publicly? No. Real Firebase is connected to the local Docker API; managed hosting remains separate work.

Finish with: "The real login flow now works, and the failure we found improved our production-default test coverage. I distinguish a successful local login from complete provider and production validation."

## Copy-and-paste booking and checkout demo

You can copy prepared commands into PowerShell during the interview. You do not need to memorise the syntax or build a frontend. Keep this guide open, paste one block at a time, explain the result, then continue. Do not paste PS or >> prompt text from a terminal transcript. Preparing a script is fine; understanding the operation and its failure behavior is what matters.

Prerequisites: local services running and $session from the Firebase walkthrough still valid. Access tokens last ten minutes; reauthenticate or use the documented refresh flow if needed. These blocks create real local demo records. Use a future service date when presenting; the example date is not a permanent business default.

### Block A: create a booking

```powershell
$booking = Invoke-RestMethod `
    -Method Post `
    -Uri 'http://localhost:8080/bookings' `
    -Headers @{ Authorization = "Bearer $($session.accessToken)" } `
    -ContentType 'application/json' `
    -Body (@{ serviceDate = '2030-01-01'; voucherCode = $null } | ConvertTo-Json) `
    -ErrorAction Stop

$booking | Select-Object id, state, totalMinor, currency
```

Say: "I send the service date, not an amount. The server calculates the price and creates a Draft booking. State 0 means Draft; this API currently serializes enums as numbers. With the current default configuration, 10,000 halalas is SAR 100."

Observed by the user: Draft state 0, totalMinor 10000, currency SAR. The price can change with trusted configuration or vouchers; do not describe the example amount as hardcoded client input. Creating another booking is a new operation, not an idempotency proof.

### Block B: create a checkout

```powershell
# Generate once for this logical checkout; keep the key for retries.
$checkoutKey = [Guid]::NewGuid().ToString('N')

$checkout = Invoke-RestMethod `
    -Method Post `
    -Uri "http://localhost:8080/bookings/$($booking.id)/checkout" `
    -Headers @{ Authorization = "Bearer $($session.accessToken)" } `
    -ContentType 'application/json' `
    -Body (@{ requestKey = $checkoutKey } | ConvertTo-Json) `
    -ErrorAction Stop

$checkout | Select-Object id, checkoutId
```

Say: "The payment attempt is saved before calling the separate gateway. This response contains the local attempt ID and the provider checkout ID. Creating a checkout does not mean money was captured."

Observed by the user: a local attempt ID and a gateway checkout ID were returned. Neither proves successful payment. Select-Object only displays named properties; a blank selected status field alone is not evidence of a payment error.

### Block C: retry and compare

```powershell
$retry = Invoke-RestMethod `
    -Method Post `
    -Uri "http://localhost:8080/bookings/$($booking.id)/checkout" `
    -Headers @{ Authorization = "Bearer $($session.accessToken)" } `
    -ContentType 'application/json' `
    -Body (@{ requestKey = $checkoutKey } | ConvertTo-Json) `
    -ErrorAction Stop

[PSCustomObject]@{
    SameAttempt = ($retry.id -eq $checkout.id)
    SameGatewayCheckout = ($retry.checkoutId -eq $checkout.checkoutId)
}
```

Expected: both True. This repeat-checkout command is prepared for the user to run; the live comparison has not yet been observed. Say: "A repeated request recovers the same attempt and checkout. This shows a simple retry; the automated concurrent-request and response-loss tests prove the harder cases."

### Block D: inspect the booking

```powershell
Invoke-RestMethod `
    -Uri "http://localhost:8080/bookings/$($booking.id)" `
    -Headers @{ Authorization = "Bearer $($session.accessToken)" } `
    -ErrorAction Stop |
    Select-Object id, state, totalMinor, currency, version
```

Before simulated payment, expect PendingPayment (state 1). Paid is state 2; Offered is state 3 and can follow asynchronously if an eligible performer exists. Do not call a checkout ID proof of Paid. The next live step is operator-authenticated fake-gateway payment and verification of the resulting booking state; it has not yet been observed for this booking.

Presentation rhythm: paste -> show the small response -> explain one guarantee -> name the corresponding failure test. Keep token variables, service-account JSON and environment secrets off screen. A five-minute demo should show the normal flow and one retry, then one focused concurrency test rather than every test or command in the repository.

## Live Azure, push and email setup: next demo milestone

Requested next: actual private cloud upload/read and actual notification receipt. These remain pending until provider setup and receipt are observed; existing local tests are not live-delivery evidence.

User decision: keep Azurite for the storage demonstration and defer live Azure. Focus on real email/FCM next. Follow [NOTIFICATION_DEMO.md](NOTIFICATION_DEMO.md) for the step-by-step Resend setup, local browser receiver, device registration and outbox-driven payment trigger. Preparation is implemented and locally checked, but real channel receipt has not yet been observed.

- Azure needs an existing subscription or suitable free credits. Do not create paid resources as an assumed extension of the no-paid-hosting constraint. For this adapter use a private Blob container, HTTPS and an account-key connection string supplied through Storage configuration. Anonymous blob access must remain disabled. Verify an upload through the API-issued exact-path link, mark it complete, obtain an owner-checked read link and demonstrate unsigned access denial using synthetic content, never a real passport.
- Email uses Resend. Without a verified custom domain, its test sender can deliver only to the email address associated with the Resend account. This is enough for a personal delivery demo; arbitrary recipients require a verified sender domain. Configure the provider key privately, plus the trusted local user-ID/recipient mapping. Use a real booking event so email leaves through the outbox, not a request-time direct-send shortcut.
- Push uses the existing Firebase project but requires a real recipient registration. A browser demo needs a local receiver/service worker, notification permission and the Firebase Web Push public VAPID key. A Firebase user ID token is not a device registration token. Register the recipient with authenticated /notifications/device, then verify receipt from an outbox-driven booking event. FCM console-only sends would test Firebase reachability but would not prove our outbox adapter.

Useful references: [Azure storage creation](https://learn.microsoft.com/en-us/azure/storage/common/storage-account-create), [Firebase web push setup](https://firebase.google.com/docs/cloud-messaging/web/get-started), [Resend testing-domain restriction](https://resend.com/docs/knowledge-base/403-error-resend-dev-domain). Provider credentials, signed blob URLs and device registration tokens must not be pasted into chat or saved in this guide.
