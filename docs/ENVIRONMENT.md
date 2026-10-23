# Environment variable reference

Names and purposes only; no credentials or example values are recorded. Storage/payment secrets remain absent until later steps; identity settings are listed below. No appsettings file contains secrets. The ignored local .env is generated once by scripts/create-local-env.py and loaded by Docker Compose, not by dotnet run.

## Service processes (API, gateway, explicit migration runners)

| Name | Requirement / purpose |
|---|---|
| ConnectionStrings__Database | Required; full Npgsql connection string. Runtime uses its schema's runtime role, migration process uses its schema-owner role. Configure finite connection/command timeouts; managed deployments require validated TLS. Never log it. |
| ASPNETCORE_ENVIRONMENT | Optional hosting environment; deployment sets production behavior. |
| ASPNETCORE_HTTP_PORTS | Container listening port; set explicitly by Compose/managed host. |
| ASPNETCORE_URLS | Optional dotnet-run URL binding override; use only when local port override is needed. Takes precedence over the port setting. |
| APP_UID | Supplied by Microsoft's runtime image; Dockerfile uses this existing non-root identity, not an application secret. |

## Local PostgreSQL / bootstrap

| Name | Requirement / purpose |
|---|---|
| POSTGRES_USER | Bootstrap administrator login, provided by Compose/container. Not given to service runtime. |
| POSTGRES_DB | Existing application database name, provided by Compose/container. |
| POSTGRES_PASSWORD | Required local database bootstrap password; ignored .env only. |
| APP_DB_PASSWORD | Required local app-runtime password. |
| GATEWAY_DB_PASSWORD | Required local gateway-runtime password. |
| APP_MIGRATOR_PASSWORD | Required local app-schema-owner password. |
| GATEWAY_MIGRATOR_PASSWORD | Required local gateway-schema-owner password. |
| PGHOST | Optional psql host when manually bootstrapping a managed database. |
| PGPORT | Optional psql port for managed bootstrap. |
| PGPASSWORD | Optional psql administrator credential supplied by the host secret manager for manual bootstrap. |
| PGSSLMODE | psql TLS verification policy for managed bootstrap. |
| PGSSLROOTCERT | Optional psql trusted-root certificate path for managed bootstrap. |

Bootstrap passwords populate role definitions; runtime processes receive only their own connection string. The initialization script runs only for a fresh local data volume. Changing .env does not rotate existing DB roles. The local generator uses hex secrets to avoid Compose/connection-string escaping ambiguity; use properly escaped full connection strings with arbitrary host-issued passwords.

## GitHub Actions / optional future deployment

| Name | Requirement / purpose |
|---|---|
| GITHUB_TOKEN | GitHub-provided Actions secret for GHCR push; job has package-write permission. Not committed or supplied to application runtime. |
| GITHUB_REPOSITORY | GitHub-provided repository identifier, normalized for image names. |
| GITHUB_OUTPUT | GitHub-provided step output file used to pass the image root. |
| HOST_DEPLOY_URL | Optional staging/production environment secret: HTTPS endpoint of a future deployment orchestrator; no provider endpoint currently configured. |
| HOST_DEPLOY_TOKEN | Optional matching orchestrator authentication secret. Missing either host secret means a clean deployment skip with a message. |
| IMAGE_ROOT | Workflow-provided input to optional deployment script, not a manually configured secret. |
| REVISION | Workflow-provided immutable image revision, not a manually configured secret. |

Create separate GitHub environments for staging and production when needed; use the same host-secret names in each. The optional orchestrator contract is in DEPLOYMENT.md. Standard inherited OS/SDK variables are not application configuration.

## Identity (API only; step 2b)

| Name | Requirement / purpose |
|---|---|
| Firebase__ProjectId | Required for auth; expected Firebase project. |
| Firebase__CredentialsJson | Required for real verification; trusted service-account JSON from the secret manager. Only service-account credential type is accepted. Never a client-supplied document. |
| Auth__Issuer | Required JWT issuer. |
| Auth__Audience | Required JWT audience. |
| Auth__SigningKey | Required independent cryptographically random base64-encoded key, decoded length exactly 32 bytes. |
| Auth__RecoveryKey | Required different base64-encoded 32-byte AES-GCM recovery key; also HMACs opaque limiter subjects with a distinct message prefix. |
| Auth__IpLimit | Optional positive per-IP fixed-minute request budget. |
| Auth__AccountLimit | Optional positive per-verified-account fixed-minute budget shared across IPs. |
| Provisioning__FirebaseIdToken | Operator command only: fresh ID token for the exact identity to provision; secret environment injection, never CLI argument/history. |
| Provisioning__Role | Operator command only: explicit staff/performer role, not Customer/System. |
| FIREBASE_AUTH_EMULATOR_HOST | Must remain unset for the real adapter; emulator mode is explicitly refused because it bypasses signature guarantees. Tests inject controlled HTTP/verifier implementations instead. |
| AUTH_SIGNING_KEY | Compose substitution source for API signing key; new local .env files generate it. |
| AUTH_RECOVERY_KEY | Compose substitution source for recovery key; new local .env files generate it independently. |
| FIREBASE_PROJECT_ID | Optional Compose substitution source for the Firebase project. |
| FIREBASE_CREDENTIALS_JSON | Optional Compose substitution source for trusted service-account JSON; quote correctly in an ignored .env or inject from shell/secret manager. |

Missing auth settings leave health available and authentication unavailable/fail-closed; there are no development signing-key defaults. Existing step-1 .env files are NOT overwritten: add auth variables using your secret manager or generate separate random keys locally, preserving all PostgreSQL passwords. Never print these values into logs or commits. Keep keys consistent across replicas/restarts so same-request recovery works; recovery key changes require coordination (wait out recovery windows or revoke families), while signing-key rotation invalidates outstanding access tokens in this single-key version.

## Step 3 payment simulation and workers

| Name | Requirement / purpose |
|---|---|
| Gateway__ApiKey | Shared private gateway API credential, required by Core HTTP client and gateway simulation endpoints; never sent to a customer browser. |
| Payments__GatewayUrl | Core's configured gateway HTTP base address; no customer-supplied URL is accepted. |
| Payments__WebhookEncryptionKey | Independent base64-encoded key whose decoded length is exactly 32 bytes; shared by gateway encoder/Core decoder. |
| Payments__WebhookSignatureKey | Different base64-encoded 32-byte HMAC key; signature covers the raw encrypted body. |
| Payments__EventReceiverUrl | Historical/test external receiver only; production internal event routing ignores this variable. |
| Payments__EventReceiverKey | Historical/test external receiver only; production internal routing ignores it. |
| Gateway__WebhookUrl | Full signed-callback URL for the gateway dispatcher. The production signed callback is mapped. |
| Gateway__ReturnUrl | Trusted server-configured browser return URL; redirects require HTTPS or loopback HTTP. Redirect does not prove payment. |
| Gateway__DispatchEnabled | Optional gateway-dispatch switch; enabled by Compose now that capacity/refunds exist. |
| Gateway__Simulation__CreateDelayMs | Optional bounded delay after checkout commit, used to model lost/late responses. |
| Gateway__Simulation__QueryDelayMs | Optional bounded query latency. |
| Gateway__Simulation__PayResponseDelayMs | Optional bounded delay after charge/event commit. |
| Gateway__Simulation__FailCreateResponse | Optional failure response after a durable checkout was created. |
| Gateway__Simulation__FailPayResponse | Optional failure response after a durable payment outcome was committed. |
| Gateway__Simulation__QueryUnavailable | Optional gateway query outage. |
| Gateway__Simulation__DeliveryCopies | Optional bounded repeated delivery of the exact persisted event body. |
| GATEWAY_API_KEY | Compose substitution source for the private gateway key. |
| WEBHOOK_ENCRYPTION_KEY | Compose substitution source for the encryption key. |
| WEBHOOK_SIGNATURE_KEY | Compose substitution source for the HMAC key. |

New local .env generation includes payment keys. For an existing .env, scripts/add-payment-env.py appends only missing payment secrets without rotating existing database/auth credentials or printing values. There is no public simulation-control endpoint: faults are trusted operator configuration. Core checkout/callback routes and durable workers are activated with capacity/refund safeguards.

## Core operations, files, delivery and telemetry

| Name | Requirement / purpose |
|---|---|
| Payments__WorkersEnabled | Explicit Core worker activation, nightly reconciliation, weekly claims and per-instance heartbeat readiness. Enabled by Compose. |
| Booking__BaseMinor | Positive server-side base fee in SAR minor units. |
| Booking__DailyLimit | Nonnegative daily capacity, snapshotted when a date counter is first created. |
| Booking__DateAddons__<yyyy-MM-dd> | Optional nonnegative date fee in minor units. |
| Gateway__Simulation__FailRefundResponse | Optional lost response after durable fake refund commit. |
| Storage__ConnectionString | Secret Azure shared-key connection; required for files. Readiness checks storage when configured. |
| Storage__Container | Private evidence container name. |
| STORAGE_CONNECTION_STRING | Optional Compose substitution for the storage connection. |
| STORAGE_CONTAINER | Optional Compose substitution for the private container. |
| Payouts__PerformerFeeMinor | Positive SAR fee snapshotted at completion, capped to booking total. |
| Payouts__Accounts__<performer UUID> | Trusted secret Saudi IBAN per performer; snapshotted on first export. |
| Notifications__EmailApiKey | Secret Resend API credential, used only by outbox delivery. |
| Notifications__EmailFrom | Provider-verified sender; never logged. |
| Notifications__EmailRecipients__<user UUID> | Operator-maintained recipient email; never used to identify/link users or grant roles. |
| EMAIL_API_KEY | Optional Compose substitution for email provider credential. |
| EMAIL_FROM | Optional Compose substitution for verified sender. |
| OTEL_EXPORTER_OTLP_ENDPOINT | Optional trusted collector endpoint; enables trace export. |
| OTEL_EXPORTER_OTLP_PROTOCOL | Optional standard exporter protocol selection. |
| OTEL_EXPORTER_OTLP_HEADERS | Optional secret collector authentication headers. |
| OTEL_SERVICE_NAME | Optional standard resource service-name override. |

Private account/recipient mappings can be supplied as hierarchical environment configuration. Compose does not enumerate private user IDs: add them through an ignored local override or managed secret injection. No actual credential values are listed. Unconfigured notification channels are not promised to deliver; persistent accountant alerts/reports remain available.

## Local notification demo helper

| Name | Purpose |
|---|---|
| DEMO_EMAIL_RECIPIENT | Temporary process environment used by configure-demo-email.ps1 to supply one trusted recipient mapping through its ignored Compose override. It is not an identity key. |

The helper temporarily supplies EMAIL_API_KEY/EMAIL_FROM and preserves Firebase settings while recreating the API. It restores the calling process environment afterward and does not persist the Resend key in its generated override. Repeat the helper when recreating the email-enabled stack. The browser receiver's ignored .env.push-demo.json stores only apiKey, projectId, messagingSenderId, appId and the public VAPID key; it must never contain Admin credentials, backend session tokens or the email provider key. These are local receiver settings rather than server environment variables. See NOTIFICATION_DEMO.md.
