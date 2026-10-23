# What to show for each backend requirement

The implemented backend is broader than the current frontend. The frontend covers customer booking/payment/notifications and privileged read-only operations. It does not have performer acceptance, proof upload/approval, completion, payout creation or CSV download screens. Those features have API handlers and automated integration proofs. Do not imply that every backend capability is clickable in the UI.

## Coverage map

| Requirement | Show in the frontend | Show as automated/backend evidence |
|---|---|---|
| Fake payment and checkout retry | Create checkout, Retry checkout, Simulate successful payment, resulting state | Double-tap, timeout and crash-recovery tests |
| Verified/duplicate/out-of-order callbacks | Paid only after the worker verifies; activity is explanatory, not proof of cryptography | PaymentIntegrationTests verify signatures, mismatches, duplicates, old events and rollback |
| Capacity | One booking is visible, but this does not prove the global limit | CapacityRefundTests: 200 verified payments, 50 slots, 150 refunds |
| Cancellation/refunds | Cancel a paid booking; observe refund status and Refunded | Lost refund response and cancellation/capture-race tests |
| Assignment/expiry | A booking may show Offered if an eligible performer exists; current UI has no accept action | AssignmentTests: active-offer exclusion, suspension, concurrent accept/expiry |
| Proof/completion | No current action screens | PrivateFileTests for real Azurite grants; PayoutTests for assigned-performer/proof/supervisor guards |
| Payout batches/CSV | No current action/download screens; staff payment ledger is not a payout screen | PayoutTests: double-run, failed-claim release, final-state race and deterministic CSV |
| Outbox delivery | Actual email inbox receipt; actual push only if a message appears | Atomic rollback, duplicate publication, retry/backoff/dead-letter tests |
| Reconciliation/jobs | Operations -> Load operations, with Admin/Accountant | DeliveryOperationsTests: unique schedules, missing-capture report and stale heartbeat |
| Health | /health/live and /health/ready | PostgresReadinessTests: real database up/down behavior |

## Part A: show the real flow (about five minutes)

1. Sign in as Customer. Show the role. Explain UID-based identity and backend ownership checks.
2. Enable push before generating a payment event. Registered only proves recipient registration.
3. Create a booking with a future date. Show the saved SAR amount; no client-supplied amount is sent.
4. Create checkout and retry it once. Explain that the same operation is recovered; local financial guarantees also depend on the provider's idempotency contract.
5. Simulate payment, refresh until capture is verified and show the resulting state. No real money moves.
6. Show the real email. Show push only if actually received. Explain the background outbox, independent channel retries and possible duplicate push after a lost acknowledgement.

### Show a real refund in the UI

Use a fresh booking so the main successful example remains easy to follow:

1. Create booking -> Create checkout -> Simulate successful payment.
2. Wait for Paid, then click Cancel booking and confirm.
3. Refresh until the booking displays Refunded and its refund status is Succeeded.
4. Say: "Cancellation and the refund request are durable. The worker retries one refund key until the provider confirms it. A request alone is not proof of a refund."

If an accepted/in-progress booking cannot be cancelled by the Customer, that is the role/state guard working; do not bypass it. Use a separate fresh booking. If a date is full the system may cancel/refund automatically, so choose a date with capacity for the normal-flow example.

Optional late-payment demonstration: create checkout, Cancel before payment, then use Simulate late payment on that cancelled booking. Observe that the booking is not resurrected and the captured money follows the refund path. This is a fake-provider edge-case control, available only in the explicitly enabled Demo environment. Prepared scenario, not a claim it has been manually performed by the user.

## Part B: show the difficult guarantees (choose two or three)

Use repository PowerShell with Docker running. Tests start isolated containers and do not mutate your live demo database. A focused test still has startup time; if the interview is short, run one and use saved VERIFICATION.md/load reports for the others. Do not run every suite simply to fill the presentation.

### How to use these scripts without getting lost

The quoted text below is a suggested spoken script, not another shell command. Paste only the PowerShell code blocks into the terminal. You may read the script from your notes; make sure you understand its meaning.

Use the same pattern each time: **explain the risk -> run the command -> show the assertion -> explain the prevention -> state the limit**.

To open a file in VS Code: press Ctrl+P, paste the repository-relative path shown below, then press Enter. Press Ctrl+F and paste the exact method/phrase to find the relevant code. Do this once before the interview and leave the useful files open in tabs. File paths and method names are more reliable than line numbers, which move when code changes. Do not read the entire file aloud.

Say while containers start: "This test starts its own real PostgreSQL database. It is separate from the database behind the frontend, so this demonstration will not erase my live demo bookings."

When the command finishes: only say it passed if the actual output says Passed and there are zero failures. A Docker error, no tests matching the filter or a skipped test is not a passing proof. If it fails, say: "This run failed; I would inspect this failure rather than claim the guarantee from a green run I did not observe." Historical counts and timings are in VERIFICATION.md, not promises about the next run.

### Payments: duplicates, webhooks, timeout and crash recovery

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter FullyQualifiedName~PaymentIntegrationTests
```

Open PaymentIntegrationTests.cs and show assertions for one provider charge, one local capture and one paid event. Point to Two_hundred_double_taps_and_pay_retries_produce_one_charge_capture_and_paid_event and Crash_after_charge_commit_is_recovered_without_another_charge. Show Old_events_cannot_undo_a_verified_success for out-of-order handling. Say: "The UI shows the normal flow; these tests force the failures that are hard to reproduce by clicking."

**Say before running:**

> "A payment response can get lost after the gateway accepts the operation. If I treat that timeout as failure and start another charge, I can charge twice. This suite repeats requests, loses responses, restarts HTTP hosts and sends duplicate or old callbacks."

**Open and explain:**

1. Ctrl+P: `tests/Labaik.IntegrationTests/PaymentIntegrationTests.cs`. Ctrl+F: `Two_hundred_double_taps_and_pay_retries_produce_one_charge_capture_and_paid_event`.
2. Point to the concurrent request setup and final assertions for one checkout/charge/capture/paid event. Say: "Many calls compete, but I check the provider ledger and our local records separately. A single local capture alone would not prove the gateway charged only once."
3. Ctrl+F: `Crash_after_charge_commit_is_recovered_without_another_charge`. Say: "The fake provider commits the charge before the response is lost. Recovery uses the existing durable attempt and key. The final assertions still require one provider charge and one local capture. This is a deterministic failure simulation, not a claim that I killed an operating-system process."
4. Ctrl+P: `src/Labaik.Infrastructure/Payments/PaymentCheckout.cs`. Ctrl+F: `BeginTransactionAsync`. Say: "We lock the booking, find or save the attempt and commit before the external call. An unresolved attempt remains blocking instead of opening a new charge."
5. Ctrl+P: `src/Labaik.Infrastructure/Payments/PaymentPump.cs`. Ctrl+F: `Re-query even`. Say: "A callback tells us to verify. We query the gateway, then validate its current result before recording payment. An old event cannot undo a confirmed success."

**Say after a passing run:**

> "The assertions passed for local and provider records under these controlled failures. A real provider must honor persistent idempotency too; the local database cannot guarantee every external effect by itself."

### Capacity and refunds under concurrency

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter FullyQualifiedName~CapacityRefundTests
```

Open Two_hundred_concurrent_verified_payments_allocate_fifty_and_refund_overflow_once. Show the assertions: 200 captures, 50 Paid/allocated, 150 cancelled refund requests, then 150 confirmed refunds. Open BookingMoney.cs and explain the conditional allocated < limit update. A simple UI booking or a 200-way checkout test alone is not proof of capacity protection.

**Say before running:**

> "The date allows fifty bookings. I prepare two hundred paid bookings for that same date and run competing capture processors. Exactly fifty can receive places; the other captured payments must be tracked and refunded."

**Open and explain:**

1. Ctrl+P: `tests/Labaik.IntegrationTests/CapacityRefundTests.cs`. Ctrl+F: `Two_hundred_concurrent_verified_payments_allocate_fifty_and_refund_overflow_once`.
2. Point to `Task.WhenAll` with 200 processors. Say: "These are independent work scopes competing against real PostgreSQL. The connection pool and database control how much SQL executes at once; I am testing two hundred competing operations, not claiming two hundred CPU threads."
3. Point to the assertions for 200 CapturedPayments, 50 Paid bookings, 150 Cancelled bookings and Allocated equal to 50. Say: "The gateway money is accounted for even when we cannot provide a place. We do not silently discard the extra captures."
4. Point to the refund pump and 150 gateway refunds/Refunded bookings. Say: "The first part checks durable refund requests. This second part checks that processing actually records confirmed fake-provider refunds. Those are different stages."
5. Ctrl+P: `src/Labaik.Infrastructure/Payments/BookingMoney.cs`. Ctrl+F: `AllocateAsync`. Point to the UPDATE containing `"Allocated"<"Limit"`. Say: "The database increments only if the current counter is below its limit. I do not read 49 in the API and then assume a slot is still free. That allocation and the booking decision commit together."
6. Ctrl+P: `src/Labaik.Infrastructure/Persistence/CoreModel.cs`. Ctrl+F: `CK_DailyCapacity_Bounds`. Say: "The CHECK constraint is a second protection: the stored counter cannot be negative or exceed its limit. The transaction also keeps the counter and the booking allocation flag consistent."

**Say after a passing run:**

> "The result is fifty allocated places and one hundred fifty confirmed refunds, with every captured payment tracked. This is a controlled fake-gateway concurrency proof, not a real bank or production throughput claim."

### Performer assignment and accept versus expiry

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter FullyQualifiedName~AssignmentTests
```

Open AssignmentFileTests.cs. Show Accept_and_expiry_race_has_one_consistent_outcome_and_accept_is_idempotent with both deadline cases. Acceptance and expiry run concurrently against PostgreSQL. Show that booking and offer agree afterward, and accepting the same valid offer again succeeds. Say: "Both paths share the database lock and clock; the losing action cannot create a contradictory assignment."

To show the algorithm, open Assignment.cs: eligible active/unsuspended performers, no active offer, lowest LastOfferedSequence, two-minute deadline and active-offer unique indexes. The UI stays Paid if no eligible performer exists; that is waiting for staffing, not proof that payment failed. A real offer needs a separately provisioned Performer identity; the UI does not fabricate one.

**Say before running:**

> "The dangerous moment is a performer accepting while an expiry worker tries to withdraw the same offer. I run both paths together and require the offer and booking to end in one consistent outcome."

**Open and explain:**

1. Ctrl+P: `tests/Labaik.IntegrationTests/AssignmentFileTests.cs`. Ctrl+F: `Accept_and_expiry_race_has_one_consistent_outcome_and_accept_is_idempotent`.
2. Point to the two InlineData values and the database deadline update. Say: "I place the deadline clearly before or after database time so the expected outcome is deterministic. This covers both sides of the deadline, rather than relying on a flaky two-minute sleep."
3. Point to `Task.WhenAll(accepting, system.Pump`. Say: "Acceptance and expiry now compete. The following assertions require Accepted/Accepted before expiry or Expired/Paid after expiry. There must not be an accepted offer attached to a booking that expiry returned to the pool."
4. Point to the final repeated `Accept()` assertion. Say: "Accepting this same valid offer again is idempotent for the same performer."
5. Ctrl+P: `src/Labaik.Infrastructure/Operations/Assignment.cs`. Ctrl+F: `AssignmentLock`. Say: "A short database mutex serializes the assignment decision. Acceptance, expiry and suspension follow the same lock order. We compare the deadline with database time."
6. In the same file, find `ORDER BY` and `LastOfferedSequence`. Say: "We exclude inactive, suspended and already busy performers, choose the least recently offered eligible performer and advance its sequence. This is a simple round-robin policy."
7. Ctrl+P: `src/Labaik.Infrastructure/Operations/OperationsModel.cs`. Ctrl+F: `UX_Offers_Active`. Say: "Partial unique indexes add a second guarantee: one active offer per booking and one active booking per performer."

**Say after a passing run:**

> "Both deadline cases passed without contradictory state, and the same accepted offer could be accepted again safely. The global mutex is deliberately simple for this assignment; I would revisit its throughput if the workload grew."

### Private proof and completion rules

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter FullyQualifiedName~PrivateFileTests
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter "FullyQualifiedName~Completion_requires_assigned_performer_and_approved_proof_and_credits_once"
```

The file test really uses Azurite: private unsigned reads fail, another user cannot obtain a read grant, changing the signed path fails and reusing upload permission cannot overwrite approved content. Explain that this is local Azure-compatible storage, not live Azure cloud.

The completion test checks an assigned performer, uploaded proof and supervisor approval before a single earning appears. Its fixture seeds file metadata to exercise workflow guards; it is not an end-to-end live video upload/approval demonstration. The separate Azurite test proves storage behavior. Do not conflate the two proofs.

**Say before the first command:**

> "The files are private, and a short-lived upload link must not become permission to overwrite approved evidence. This test makes real HTTP requests to Azurite instead of pretending a URL is secure."

**Open after the first command:**

1. Ctrl+P: `tests/Labaik.IntegrationTests/AssignmentFileTests.cs`. Ctrl+F: `Private_create_only_SAS_is_exact_path_no_overwrite_and_owner_checked`.
2. Point to the successful upload, changed-path denial, overwrite denial, stranger-read exception and unsigned-read denial. Say: "A valid link permits only its exact operation/path. The API also checks who is requesting read access. The test verifies those separate controls."
3. Ctrl+P: `src/Labaik.Infrastructure/Operations/AzurePrivateBlobStore.cs`. Ctrl+F: `BlobSasPermissions.Create`. Say: "The upload grant has Create permission without Write. Reusing the grant cannot replace the existing object. Read permission is issued separately and the links are configured to expire after five minutes. We do not wait five minutes in this test to prove elapsed expiry."
4. Ctrl+P: `src/Labaik.Infrastructure/Operations/PrivateFiles.cs`. Ctrl+F: `ReadAsync`. Say: "Before returning a read grant we check ownership or role and compare the saved object identity. The container itself remains private."

**Say before the completion command:**

> "A private upload is only one part of completion. The assigned performer must submit proof, and a supervisor must approve it before completion creates an earning. I test those business guards separately from the storage adapter."

**Open after the completion command:**

1. Ctrl+P: `tests/Labaik.IntegrationTests/PayoutTests.cs`. Ctrl+F: `Completion_requires_assigned_performer_and_approved_proof_and_credits_once`.
2. Point to the denied Customer start, the `Complete` test helper above it, the missing-proof/unapproved-proof exceptions, repeated completion and single earning assertion. Say: "The helper builds the business preconditions step by step, so rejected actions are tested as well as the successful path. It seeds file metadata for these rule checks; it is not uploading a real video in this test."
3. Ctrl+P: `src/Labaik.Infrastructure/Operations/Payouts.cs`. Ctrl+F: `MoveAsync`. Say: "This workflow checks the actual actor and proof, then uses the one booking state machine. Completion and earning creation belong to one transaction."

**Say after both passing checks:**

> "Private storage behavior and completion guards passed as separate proofs. This uses local Azurite, not live Azure, and it does not claim malware scanning or a full video-processing pipeline."

### Payout double-run, failure recovery and CSV

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter FullyQualifiedName~PayoutTests
```

Open Concurrent_batch_creation_never_claims_an_earning_twice. It runs competing batch creations and asserts one batch/item claim for the earning. Show Export_is_repeatable_and_not_payment_failed_claim_can_be_retried_paid_cannot: same CSV bytes, export leaves Pending, confirmed Failed releases the claim, confirmed Paid cannot be paid again. Explain the partial unique earning-claim index in PayoutModel.cs.

Say: "The API and CSV generation are implemented and tested, but the current frontend has no payout action screen. The CSV is a bank instruction format; it does not execute a transfer, and bank-specific acceptance remains unverified." This is a truthful backend demonstration, not a live banking claim.

**Say before running:**

> "Two workers could try to include the same earning in different payout batches. This suite checks that one earning gets one live claim, that confirmed failure releases it, and that exporting a CSV is not treated as a completed bank payment."

**Open and explain:**

1. Ctrl+P: `tests/Labaik.IntegrationTests/PayoutTests.cs`. Ctrl+F: `Concurrent_batch_creation_never_claims_an_earning_twice`.
2. Point to `Enumerable.Range(0,20)` and the two InlineData cases. Say: "Twenty batch requests compete, both with matching request keys and with different keys. The assertions still require one batch, one payout item and one reserved earning. A different request key cannot bypass the earning claim."
3. Ctrl+F: `Export_is_repeatable_and_not_payment_failed_claim_can_be_retried_paid_cannot`. Point to matching export bytes and Pending status. Say: "Exporting twice gives the same instruction file, and neither export says that the bank has paid."
4. Point to Failed -> new batch -> Paid, the rejected conflicting final result and the final null new-batch result. Say: "Only a confirmed failure releases the earning. A paid earning cannot be claimed again, and an unknown bank outcome must remain Pending instead of being retried as a new transfer."
5. Ctrl+P: `src/Labaik.Infrastructure/Operations/PayoutModel.cs`. Ctrl+F: `UX_Items_ClaimedEarning`. Say: "This partial unique index applies to Pending and Paid items. It prevents two live financial claims on an earning. Failed items remain historical and allow recovery in another batch."
6. Ctrl+P: `src/Labaik.Infrastructure/Operations/Payouts.cs`. Ctrl+F: `CreateAsync`, then `ConfirmAsync`, then `ExportAsync`. Say: "Creating reserves the earnings, confirmation applies a verified operator-recorded bank result, and export only produces a deterministic CSV. Those are deliberately different operations."

**Say after a passing run:**

> "The local claim and accounting rules passed. There is no real bank transfer in this test and the current frontend has no payout screen. Actual bank execution and acceptance of its CSV format remain external work."

### Outbox atomicity, retries, dead letters and restart

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter "FullyQualifiedName~Paid_capture_and_outbox_roll_back_together_when_outbox_insert_fails|FullyQualifiedName~Publisher_retries_backoff_then_dead_letters_without_false_success|FullyQualifiedName~Worker_crash_releases_work_after_lease_and_stale_owner_cannot_commit"
```

Show assertions: failed outbox insertion rolls back financial/state changes; repeated publication failure reaches a visible dead letter; a new worker can reclaim an expired lease while an old owner cannot commit. Explain that an external send can still repeat, so this is not universal exactly-once delivery. A real email receipt proves that one delivery worked; it does not prove all these failure properties.

**Say before running:**

> "My real email demonstration shows that one message was delivered. These three tests check the more important failure boundaries: losing the outgoing intent, retrying a failing receiver and recovering work after its owner stops making progress."

**Open and explain:**

1. Ctrl+P: `tests/Labaik.IntegrationTests/PaymentIntegrationTests.cs`. Ctrl+F: `Paid_capture_and_outbox_roll_back_together_when_outbox_insert_fails`.
2. Point to the deliberate outbox-insert rejection and assertions for zero captures, zero outbox rows, PendingPayment and an unprocessed inbox. Say: "I force the database write to fail. These assertions show that we cannot commit Paid while losing the outgoing work. After removing the fault, the same durable work can succeed."
3. Ctrl+F: `Publisher_retries_backoff_then_dead_letters_without_false_success`. Point to receiver rejection, retry and DeadLetter/PublishedAt assertions. Say: "A failed send does not become published. After the configured test retry limit, the job becomes a visible dead letter. The test uses a small retry policy and a controlled receiver, not my actual inbox."
4. Ctrl+F: `Worker_crash_releases_work_after_lease_and_stale_owner_cannot_commit`. Point to the first claim, forced lease expiry, higher second claim version and null result when locking the old claim. Say: "I simulate an abandoned lease. Another worker claims it with a newer version, and the old owner cannot commit application effects. This test does not literally kill a process."
5. Ctrl+P: `src/Labaik.Infrastructure/Payments/WorkQueue.cs`. Ctrl+F: `ClaimAsync`, then `LockAsync`. Say: "SKIP LOCKED lets workers choose available rows without waiting on each other. Persisted owner, expiry and claim version matter after the claim transaction commits; a row lock by itself is not a permanent job ownership guarantee."

**Say after a passing run:**

> "The database intent is atomic and job recovery is fenced. Network delivery may repeat if sending succeeded but its acknowledgement was lost, so provider idempotency or duplicate-tolerant consumers are still needed."

### Reconciliation and health

```powershell
dotnet test tests/Labaik.IntegrationTests --configuration Release --filter "FullyQualifiedName~DeliveryOperationsTests|FullyQualifiedName~PostgresReadinessTests"
```

The report test deliberately leaves a gateway capture missing locally and verifies that reconciliation reports it without inventing local money. The heartbeat test proves that another healthy worker cannot mask this instance's stale heartbeat. The readiness test uses real PostgreSQL and shows ready when reachable, not-ready when down.

For an Admin/Accountant already provisioned through the verified-UID CLI, sign in fresh and use Operations -> Load operations. Show actual payment rows, reconciliation counts/mismatches, job counts, heartbeat and dead letters. An empty mismatch/dead-letter list is a valid clean result, not a staged failure demonstration. Customer accounts cannot access these views. Provisioning instructions are in FRONTEND_DEMO.md; do not grant a role through SQL or a public endpoint just for a screenshot.

**Say before running:**

> "A running process can still have a broken database or a stuck worker. I test readiness against real dependencies and compare provider money records with the local ledger rather than assuming both systems always agree."

**Open and explain:**

1. Ctrl+P: `tests/Labaik.IntegrationTests/DeliveryOperationsTests.cs`. Ctrl+F: `Nightly_schedule_is_unique_across_instances_and_report_exposes_lost_capture_without_fabricating_money`.
2. Point to concurrent scheduler calls, one reconciliation job and the MissingLocalCapture report. Say: "The gateway has a charge before local recovery records it. Reconciliation reports the difference but does not invent a local capture. The normal recovery path repairs the missing local record, and a later comparison is clean."
3. In the same file, find `Readiness_rejects_this_workers_stale_database_heartbeat_even_if_another_worker_is_alive`. Say: "I make one instance's heartbeat stale while another is fresh. Readiness must fail for the stale instance, so a healthy neighbour cannot hide its problem."
4. Ctrl+P: `tests/Labaik.IntegrationTests/PostgresReadinessTests.cs`. Ctrl+F: `Both_services_are_ready_only_while_real_PostgreSQL_is_reachable`. Near the end, show database stop and the HTTP status assertions. Say: "The database is actually stopped in the isolated test. Ready becomes unavailable while Live still says the process is responding. I do not stop the database serving my frontend demo."
5. Ctrl+P: `src/Labaik.Infrastructure/Operations/Reconciliation.cs`. Ctrl+F: `MismatchCount`. Say: "The report compares captures/refunds and stores a bounded result. It observes a moving system, so recent mismatches need a recheck after workers drain. Reporting does not bypass verified payment processing."
6. Ctrl+P: `src/Labaik.Infrastructure/Operations/OperationalHealth.cs`. Ctrl+F: `WorkerReadiness`. Say: "The heartbeat check uses database time and this worker's identity. It is a readiness signal, not proof that every external provider is healthy."

**If showing the staff UI:**

> "These are actual current ledger and job observations. An empty mismatch list means this report found no differences in its scope. Empty dead letters are a clean result; the fault tests show what happens when failures occur. The backend checks my current staff role, not just whether the browser displays a tab."

**Say after a passing run:**

> "The controlled dependency failures and report conditions passed. This is local operational evidence, not a claim that public hosting or every provider integration has been verified."

## Short answers when the interviewer stops you

| They ask | Say |
|---|---|
| What is Arrange/Act/Assert? | "Prepare the situation, execute the action, then check the business result. I will point to those three parts in this test." |
| What is an assertion? | "An executable expectation. If the database has 51 allocated places instead of 50, this assertion makes the test fail." |
| What does Task.WhenAll do? | "It waits for multiple started asynchronous operations. Here those independent calls compete; it is not a database lock or a promise of one thread per call." |
| Why inspect both gateway and local rows? | "A local deduplication rule could hide duplicate provider charges. I need to check both sides of the boundary." |
| Why not click the UI 200 times? | "A reproducible test controls concurrency and checks database invariants more precisely than manual clicking." |
| Why show code after a green run? | "The assertions define what the test proved. Green output without knowing the setup and assertions would be a weak explanation." |
| Does this test prove every possible interleaving? | "No. It proves the controlled cases we execute; the locking/constraints are the general mechanism, and these tests exercise important failure paths." |
| Can we see a full performer or payout UI? | "Not in the current frontend. The backend endpoints and proofs exist; those additional action screens remain frontend work." |

If you forget syntax, use this sentence: "Let me show the assertion and the rule it checks." Then find the method name above. Do not improvise an exactly-once, live-cloud or real-bank claim just to fill silence.

## Suggested final sentence

"The frontend demonstrates the normal booking and delivery flow. PostgreSQL integration tests demonstrate the races and recovery rules. Performer and payout action screens are not part of this frontend yet; those backend flows are implemented and tested. Real charges, bank transfers, live Azure and public deployment are not claimed."

This guide adds presentation instructions only; no tests were rerun when writing it. Use VERIFICATION.md for observed past results and report the actual output when you run a focused demo test.
