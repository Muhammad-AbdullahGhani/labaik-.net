# Demo preparation - plain-language notes

Keep this beside the code. Updated as implementation and real checks finish. Step 3 is complete locally: 28 new payment tests passed, 0 failed/skipped; Release build passed with 0 warnings/errors. Older suites were not rerun this turn, as requested. Public Core payment activation is still reserved for step 4.

## My opening (about 30 seconds)

"Labaik Core is a booking backend for an Umrah-by-proxy service. I focused first on protecting money and data when requests overlap, services restart, or a response gets lost. The API and fake gateway are separate HTTP services, using separate PostgreSQL schemas. I use database rules and transactions so the same rules hold when there is more than one server."

## Suggested presentation order (about 8 minutes)

1. Explain the product and the failure problem in one sentence each.
2. Show the four source layers and separate fake gateway; avoid reading every file.
3. Show the booking state diagram and one allowed/one denied transition.
4. Show integer pricing and one rounding example.
5. Explain identity using project plus Firebase UID; email is not an account key.
6. Show the verified new payment tests: double taps, a lost gateway response, repeated/old callbacks, then a worker restart.
7. Finish with what is still disabled and the next step: capacity and automatic refunds.

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

- No public hosted deployment or live Firebase project exchange has been verified.
- No real bank transfer, real HyperPay charge or actual customer card processing exists.
- Public checkout/capture remains disabled until step 4 adds capacity and refund safeguards.
- Capacity allocation, automated refunds, assignment, payouts, files and notifications are not finished.
- Only the new step-3 suite was run this turn; older test results are historical. A 200-way checkout test is NOT proof of daily-capacity protection; that application logic is next.

## Commands and evidence

See VERIFICATION.md for observed commands/results, DESIGN.md for decisions, and AI_LOG.md for AI assistance. The step-3 targeted command and final counts will be added after the new suites run. Use test-generated data; never display environment files, service-account JSON, API keys or tokens during the demo.

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

**What if a paid booking was cancelled?** "I retain the capture and the cancellation, and record refund and accountant-alert intents. Actual automatic refunds are step 4; I have not pretended those intents already returned money."

**What if the worker dies after sending?** "The receiver might see the same message again. I send the same message ID every time so a durable receiver can recognise the duplicate. The test receiver stores one receipt even when it receives two deliveries."

**Why are payment routes disabled?** "Opening checkout before date capacity and refund handling would expose an unfinished money flow. The internal implementation is reviewable and tested now; activation follows those safeguards."

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
