# Guided local demo workspace

The frontend replaces repeated PowerShell requests with one guided page. It uses the real API for login, server-priced bookings, idempotent checkout, simulation submission and verified status. Email and push still come from the existing outbox. Automated browser tests use synthetic responses; actual provider evidence comes separately from the user's live walkthrough below.

Live follow-up: the user started the real frontend, successfully registered browser push and received the payment-confirmation email from the Resend onboarding sender. Real email receipt is now user-verified. Actual push receipt remains unreported; earlier pending-walkthrough wording describes preparation. No public deployment or real payment is implied.

## Start it

Keep the current email-configured API running. In repository PowerShell run:

```powershell
& .\scripts\start-demo-ui.ps1
```

On first run it asks for public Web app settings:

1. Firebase Console -> Project settings -> General -> Your apps -> your Web app -> SDK configuration. Copy apiKey, messagingSenderId and appId into the prompts. This is the Web app config, not service-account JSON.
2. For push, Project settings -> Cloud Messaging -> Web configuration -> Web Push certificates -> Generate key pair, then copy the PUBLIC key. If unavailable, leave the VAPID prompt empty and finish login/email first.

The helper preserves current server settings, including the configured email key/recipient mapping and Firebase Admin credential. Private values pass only through temporary process environment variables to the container, never into generated override files or browser configuration. Public Web app fields are saved in ignored .env.demo-ui.public.json so later starts need no re-entry. No existing database password is regenerated.

To add/change the public push key or other Web settings later:

```powershell
& .\scripts\start-demo-ui.ps1 -Configure
```

Open **http://localhost:8080/demo/**. Sign in with your Firebase demo email/password. You do not paste a Firebase ID token, local JWT, gateway key or email provider key into this page. Own API session credentials and Firebase Auth state stay in memory, so a reload requires sign-in. FCM uses the browser's service-worker/device registration storage. Sign-out is local to this browser; it is not a global session-revocation feature.

## Five-minute presentation

1. **Sign in.** Show Customer in the header. Explain Firebase authentication versus local authorization. New identities cannot request a staff role.
2. **Enable push before payment.** Open Notifications, choose English or Arabic and click Enable browser push. Accept browser permission. Registered does not mean a message was received yet.
3. **Create a booking.** Pick a future date and optional voucher. The server calculates the total. The page sends no amount.
4. **Create checkout.** Explain intent saved before provider calls. Click Retry checkout once and explain stable keys; no second charge is created.
5. **Simulate successful payment.** This is a deliberately enabled local fake-provider action, not a real purchase. The button does not mark Paid. Wait for the existing worker to re-query, verify and commit capture/state/capacity/outbox. Status refreshes every five seconds, or use Refresh status.
6. **Show receipt.** Show the payment-confirmation push in the page and email in your own Resend-account inbox. Do not claim delivery from Paid or Registered alone. A published event from before recipient setup does not automatically send to new recipients; use a fresh event.
7. **Show one failure proof.** Run a focused capacity/offer/payout test, or cancel a fresh pending booking before clicking Simulate late payment and observe the refund workflow. No real money moves.

Keep the page foreground for the first push receipt. Background notification display depends on the browser/OS settings; Firebase's service worker displays notification payloads without a duplicate custom handler. Email sending on the Resend test domain is restricted to your Resend-account email. If you reconfigure email using its separate helper, run start-demo-ui.ps1 again afterward to restore the explicit Demo environment/UI gate.

## Optional staff operations view

Customer access intentionally hides Operations. Admin/Accountant can view captured payments, reconciliation reports, job-status counts, the latest worker heartbeat and dead letters. API routes independently recheck current database role/status; hiding a tab is not an authorization boundary. Rows are limited to the latest 50 bookings/captures and existing bounded reports; no complete-history/pagination claim is made.

Provision a dedicated Admin demo identity only through the existing verified-UID operator command. If you already have a fresh Firebase ID token for the exact identity in your private PowerShell variable, use this operator-only block:

```powershell
$env:Provisioning__FirebaseIdToken = $firebaseLogin.idToken
$env:Provisioning__Role = 'Admin'
try {
    docker compose exec -T `
        -e Provisioning__FirebaseIdToken `
        -e Provisioning__Role `
        api dotnet Labaik.Api.dll --provision-user
    if ($LASTEXITCODE -ne 0) { throw 'Provisioning failed; do not claim Admin access.' }
}
finally {
    Remove-Item Env:Provisioning__FirebaseIdToken -ErrorAction SilentlyContinue
    Remove-Item Env:Provisioning__Role -ErrorAction SilentlyContinue
}
```

This verifies that token's UID; it does not select an account by email. The command explicitly changes that identity's role and revokes its refresh families. Sign out and sign in again afterward to obtain fresh local role claims. If the old Firebase ID token has expired, obtain a new one through the documented Firebase login; never grant a role through direct SQL or a public endpoint for the demo. This optional live provisioning has not been executed by the agent.

## Boundaries you can explain

- The frontend is plain HTML/CSS/JavaScript hosted by the API, with Firebase SDK scripts from Google's pinned CDN. No React build chain or new production package is needed for this small guided workflow.
- It uses the same origin as the API, so no permissive CORS policy is introduced. This is local hosting, not the bonus Vercel deployment. A separately hosted frontend needs an explicit approved-origin policy and real deployment verification.
- Demo endpoints require both ASPNETCORE_ENVIRONMENT=Demo and Demo__Enabled=true. Production startup exposes neither the UI nor the simulation proxy, even if only the flag is accidentally set.
- The server-side simulation proxy checks current user status, role and booking ownership, derives the saved provider checkout and holds the private gateway key. No client-supplied monetary total, destination URL or key is accepted. It updates the fake gateway only; the normal capture verification path remains authoritative.
- Amounts in demo read DTOs are strings, so JavaScript does not round long minor-unit values. BigInt is used for display only; financial calculations stay in the backend.
- Checkout retries keep their per-booking request key. Refresh rotation keeps the same parent/key through an uncertain response, matching the backend's 15-second recovery contract. A late retry may require sign-in again; the client must not invent a new key to bypass replay detection.
- The activity list reflects this browser session, not a durable financial audit. Job heartbeat timestamps are observations, not universal proof that all jobs/providers are healthy.

## Observed checks

Six DemoUiTests passed against real PostgreSQL and separate fake-gateway HTTP application. They cover the double enablement gate, static/public config safety, JWT/ownership/staff access, disabled accounts and the fact that simulation creates a provider charge while only the worker records local capture. The worker test also repeats simulation and asserts one provider charge.

Playwright with installed Edge exercised login, server-price request shape, same-key checkout retry, Paid display, hidden staff view, memory-only local session, sign-out and a 390-pixel viewport with no horizontal overflow. Firebase/API responses in those browser checks are synthetic, not live provider validation. Desktop/mobile screenshots are in ignored artifacts/ui-check. Reproduce with:

```powershell
npm install --prefix artifacts/ui-check playwright --no-audit --no-fund
node scripts/check-demo-ui.cjs
```

This is a developer-only browser-check dependency, not a frontend runtime dependency. PLAYWRIGHT_BROWSER_CHANNEL can select another installed Playwright-supported browser channel. No full regression was rerun for this UI iteration; CI still runs its full backend suite.
