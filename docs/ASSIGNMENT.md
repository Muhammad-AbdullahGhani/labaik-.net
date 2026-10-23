Labaik Badal – Advanced .NET Assignment
About the job
Labaik Badal is an Umrah-by-proxy platform: ASP.NET Core API, background jobs, SQL, HyperPay payments, Firebase
sign-in and push, Azure hosting. This assignment reflects the hardest problems in our backend — money, concurrency,
reliability and security. AI tools (Claude Code, Codex, Cursor, Copilot) are encouraged — we judge your design
decisions, correctness under failure, and whether you can defend every line.
The product: “Labaik Core” booking & payments
service
Build it with .NET 10, PostgreSQL (EF Core migrations), and clean, testable architecture.
1. Identity
Sign-in with Firebase ID tokens (verify with the Firebase Admin SDK: signature, project, expiry, revocation
check) → issue your own short-lived JWT (10 min) + rotating refresh tokens (hashed in DB, reuse detection
revokes the family, grace window for lost responses).
Roles: Customer, Performer, Supervisor, Accountant, Admin. Never link a Firebase login to a staff/performer
account by email.
Rate limiting per IP + account; lockout that can’t be abused to lock out other users.
2. Bookings – a strict state machine
Refunded ,
Draft → PendingPayment → Paid → Offered → Accepted → InProgress → ProofSubmitted → Completed , plus
Cancelled ,
Disputed .
Every transition goes through one state-machine component with explicit allowed transitions and roles; invalid
transitions return 409 with a reason.
Optimistic concurrency (row version) on bookings; concurrent updates must never produce a mixed state.
Price is calculated on the server only (base fee + date add-ons + voucher), stored as minor units (halalas),
never floats.
Daily capacity per date (e.g. max 50 bookings) that cannot be exceeded under concurrency — and decide
what happens to a payment that arrives for a full date.
3. Payments (simulate HyperPay)
Build a fake payment gateway service (separate small project or an in-process mock with configurable latency,
failures and duplicate webhooks) that mimics HyperPay’s COPYandPAY: create checkout → customer pays → redirect
+ webhook (AES-GCM-encrypted body with an HMAC/signature header).
Idempotent checkout: retries or double taps reuse the live attempt; the customer can never be charged twice
for one booking.
Webhook handling: verify signature, decrypt, deduplicate, process out of order safely, and re-query the
gateway before marking paid. Amount and currency must match.
Outbox pattern: the “paid” state change and the follow-up events (offer to performer, email, push) are written in
the same DB transaction and published by a background dispatcher.
Payment arriving after cancellation → automatic refund request + accountant alert.
Reconciliation job: nightly compare gateway transactions vs. DB; report mismatches.
Vouchers: percentage / fixed, max uses, per-user limit, expiry — atomic redemption under concurrency.
4. Performer assignment
Round-robin offers with a 2-minute expiry, executed by background jobs; one active job per performer;
suspended performers excluded.
Accept vs expiry race must be impossible to corrupt (show how with a test that runs them concurrently).
Accept is idempotent for the same performer.
5. Payouts
Weekly payout batch for completed bookings; each earning paid exactly once (concurrent batch creation must
not double-pay); payout status transitions only
Export the batch as a bank CSV/XLSX file.
6. Files
Pending → Paid | Failed ; Failed earnings return to the pool.
Proof videos and passport scans in private blob storage (Azure Blob / S3 / Cloudflare R2 / Firebase Storage).
Upload via short-lived pre-signed URLs limited to the exact path; reads via short-lived signed URLs after an
ownership check. No overwrites of approved files.
7. Notifications
FCM push and email sent only from the outbox dispatcher (never inside the HTTP request), with retries +
backoff, dead-letter after N failures, invalid FCM tokens removed, localized templates (Arabic/English).
8. Background processing & reliability
Background jobs (Hangfire, Quartz or a hosted-service design — justify your choice) with distributed locking so
two instances never run the same job.
Health checks (DB, storage, job heartbeat), structured logging with correlation IDs, no PII or card data in logs,
OpenTelemetry traces.
Graceful shutdown; jobs resume after restart.
9. Testing
Unit tests for the state machine, pricing and voucher rules.
Integration tests with Testcontainers (PostgreSQL) covering: double payment, duplicate & out-of-order
webhooks, accept vs expiry race, capacity under concurrency, payout double-run.
A small load test (k6 or NBomber) proving capacity and voucher limits hold at 200 concurrent requests.
10. Deploy & operate
Dockerfile (multi-stage, non-root) and deploy to Render or Railway with managed PostgreSQL; the fake
gateway deployed too.
GitHub Actions: build, tests (incl. Testcontainers), security scan (
to a staging service on
dev-main and production on
dotnet list package --vulnerable ), deploy
main , with zero-downtime migrations (expand/contract).
Secrets only in environment variables; document every variable (no values).
Bonus: a small admin dashboard on Vercel (any framework) showing bookings, payments, reconciliation
mismatches and job health — CORS locked to that domain, admin-only.
11. Write-up
A DESIGN.md covering: architecture, the state machine diagram, how double payment / races / duplicate webhooks
are prevented, failure scenarios you tested (crash after charge, gateway timeout, webhook before redirect), trade-offs
you made, what you’d do next — and “How I used AI”: what you asked, what you accepted, changed or rejected and
why.
