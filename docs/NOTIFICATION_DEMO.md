# Real email and browser push demo

Status: preparation and local checks only. Real Resend email receipt and FCM browser receipt must be observed before calling live delivery verified. Storage stays on the existing Azurite-tested path; no Azure trial is needed for this notification demo.

## 1. Resend setup

Create/sign in to a [Resend account](https://resend.com/). Use an inbox you control. In API Keys, create a key with Sending access and keep it private. Do not paste the key into chat, the repository, a screen-shared terminal command or the browser receiver.

For this demo the sender is onboarding@resend.dev. It can send only to the email address associated with your Resend account. A custom verified domain is needed for other recipients; no domain purchase is required for the own-inbox test. [Provider restriction](https://resend.com/docs/knowledge-base/403-error-resend-dev-domain).

In the PowerShell session with a valid $session from our Firebase login, run:

```powershell
& .\scripts\configure-demo-email.ps1 -AccessToken $session.accessToken
```

The script verifies /auth/me, prompts for your own Resend-account email and the hidden sending key, writes an ignored Compose override containing environment references, and recreates the API. It preserves the running Firebase configuration. It does not link users by email or change a role. It sends no email directly. Credentials enter the container environment and are not printed or embedded in the generated override.

If /auth/me returns 401, obtain a fresh session using the Firebase walkthrough first. If PowerShell blocks local script execution, use the documented policy allowed on your own machine; do not disable organization-managed protections. Rerun this helper when recreating the email-enabled demo API: normal Compose startup without its generated override removes the recipient mapping. Its temporary environment values are restored after setup, so the override alone is not a persistent secret store.

## 2. Prepare a real FCM browser recipient

Use desktop Chrome or Edge for the demonstration. In Firebase Project settings -> General -> Your apps -> your Web app, find apiKey, projectId, messagingSenderId and appId. These are web-client settings, not the Admin SDK private JSON.

In Project settings -> Cloud Messaging:

1. Check Cloud Messaging API (HTTP v1) is enabled; use its Manage link to enable it if needed.
2. Under Web configuration -> Web Push certificates, generate a key pair if none exists.
3. Copy the PUBLIC key. Never give the browser the private key or service-account JSON.

Open a second PowerShell terminal in the repository:

```powershell
python scripts/serve-push-demo.py --configure
```

Enter the four requested Firebase Web app settings and public VAPID key. The helper saves them only in ignored .env.push-demo.json and serves a strict asset list on loopback port 8090. No Admin keys, API access JWTs or Resend credentials belong in that configuration. Keep this terminal running; the service worker needs its configuration on restart.

Open http://localhost:8090 in the browser. Click Enable notifications and allow the browser permission. Click Copy token privately. The token stays masked on the page and is not logged. Foreground notifications appear in the page's received list; background notification payloads are displayed by Firebase's worker rather than a second custom notification handler. Browser/OS notification settings can suppress an OS banner, so keep the page foreground for the first receipt check.

For later runs, configuration is retained locally:

```powershell
python scripts/serve-push-demo.py
```

The pinned Firebase JS SDK comes from Google's CDN; internet access is required. The receiver has no public backend mutation endpoints and sends no booking or email request. [Firebase web setup](https://firebase.google.com/docs/cloud-messaging/web/get-started), [foreground/background behavior](https://firebase.google.com/docs/cloud-messaging/web/receive-messages).

## 3. Register the token with the authenticated API

Back in the original PowerShell window with a valid local $session, run this block and paste the copied token at the hidden prompt:

```powershell
$deviceSecret = Read-Host 'Paste browser FCM registration token (hidden)' -AsSecureString
try {
    Invoke-RestMethod `
        -Method Post `
        -Uri 'http://localhost:8080/notifications/device' `
        -Headers @{ Authorization = "Bearer $($session.accessToken)" } `
        -ContentType 'application/json' `
        -Body (@{
            token = ([System.Net.NetworkCredential]::new('', $deviceSecret)).Password
            locale = 'en'
        } | ConvertTo-Json) `
        -ErrorAction Stop | Out-Null
    Write-Output 'Browser recipient registered. Actual push receipt is not verified yet.'
}
finally {
    Remove-Variable deviceSecret -ErrorAction SilentlyContinue
}
```

The token identifies a delivery destination. It is different from the Firebase ID token used for login and the local JWT used for API authorization. Choosing ar instead of en selects Arabic templates for subsequent notifications. No direct FCM Console send can prove our outbox path; trigger a booking event below.

## 4. Trigger one fresh paid event after both channels are configured

Use the create-booking and checkout blocks in DEMO_PREP.md. If the current booking has not yet been paid, it can be used; if its paid event was already published before channels were configured, create a new booking. Existing published events are not automatically replayed for newly added recipients.

The following simulation is an operator-only local action. The gateway key must never go into browser code or a customer app. Read the existing ignored local key without displaying it:

```powershell
$gatewayEntry = Get-Content -LiteralPath '.env' |
    Where-Object { $_ -match '^GATEWAY_API_KEY=' } |
    Select-Object -Last 1
if (!$gatewayEntry) { throw 'Missing local gateway key.' }
$gatewayKey = ($gatewayEntry -split '=', 2)[1].Trim().Trim('"').Trim("'")
try {
    $null = Invoke-RestMethod `
        -Method Post `
        -Uri "http://localhost:8081/checkouts/$($checkout.checkoutId)/pay" `
        -Headers @{ 'X-Gateway-Key' = $gatewayKey } `
        -ContentType 'application/json' `
        -Body (@{ outcome = 'Succeeded'; redirect = $false } | ConvertTo-Json) `
        -ErrorAction Stop
    Write-Output 'Fake payment submitted. Workers must verify it before changing the booking.'
}
finally {
    Remove-Variable gatewayEntry, gatewayKey -ErrorAction SilentlyContinue
}

Invoke-RestMethod `
    -Uri "http://localhost:8080/bookings/$($booking.id)" `
    -Headers @{ Authorization = "Bearer $($session.accessToken)" } `
    -ErrorAction Stop |
    Select-Object id, state, totalMinor, currency
```

Repeat only the final GET if processing is still pending. With no eligible performer, Paid is state 2; an offer may move it to state 3. The gateway's signed callback and/or recovery job re-query and verify capture, save Paid plus outbox intent, then route separate email/push jobs. No real charge occurs.

## 5. Collect actual evidence

- Check your Resend-account inbox, including spam, for the payment-confirmation email. Provider accepted is not the same as recipient inbox receipt; check the Resend delivery dashboard if needed.
- Check the local browser receiver list for the payment-confirmation push. A registered token alone is not proof of receipt.
- Show the booking state and explain the atomic Paid/outbox commit. Each recipient/channel retries independently; failure of one channel does not undo the capture or block the other.
- A sending failure retries with backoff and ultimately becomes a dead-letter job. Logs expose only HTTP status or FCM error category, not provider bodies, addresses or tokens. A privileged /operations view can inspect dead letters; the Customer session cannot access it.
- Do not create another payment to repair notification delivery. Investigate the existing delivery job and preserve its identity. If setup preceded an already dead-lettered notification, a new booking is a new demo event, not proof that the failed job recovered.

Once actual receipt is observed, record only the event type, channel, date and result in VERIFICATION.md. Do not store personal email, token, service-account key or signed URLs. Resend own-inbox testing is limited delivery validation, not a production sender-domain setup.

## Explain it in the interview

"A verified payment saves its booking update and outgoing event together. The worker creates one delivery job per recipient/channel and sends later. Here I can show a real email in my inbox and a real push in the browser; retries use durable state and the same message identity. Push can still duplicate if acknowledgement is lost."

Only use that final wording after both receipts actually succeed. Currently this document describes the prepared procedure, not a completed live run.
