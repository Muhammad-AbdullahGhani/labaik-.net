# Labaik Core

Lean steps 1-7: .NET 10/PostgreSQL backend with Firebase identity, rotating sessions, server pricing, idempotent simulated payments, capacity/refunds, performer assignment, private evidence files, guarded completion, payout claims and durable delivery/operations. API and fake gateway are separate HTTP services with separate schema credentials.

- [Local run and manual deployment](docs/DEPLOYMENT.md)
- [Environment variable names and auth setup](docs/ENVIRONMENT.md)
- [Actual verification](docs/VERIFICATION.md)
- [Plain-language demo preparation](docs/DEMO_PREP.md)
- [Guided local frontend](docs/FRONTEND_DEMO.md): login, booking, safe checkout retry, fake payment, status, push and privileged operations.
- [Design](docs/DESIGN.md), [plan](docs/PLAN.md), [AI log](docs/AI_LOG.md)

After one-time `python scripts/create-local-env.py`, run `docker compose up --build --wait`. Requires Linux Docker. Existing .env files are preserved; see identity setup for adding auth configuration. Runtime startup never applies migrations: Compose runs separate tasks first. Without Firebase/auth settings, health works and authentication fails closed. No production fake verifier exists.

Existing local env files can add missing payment secrets with `python scripts/add-payment-env.py` without rotating database passwords. Compose enables workers and signed callback delivery. Firebase credentials, private Azure storage and notification destinations must be configured for live user/file/delivery flows; there is no public test login or role-grant endpoint.

The final full Release regression passed 823 tests with no failures/skips, including three NBomber checks at 200 users. Subsequent targeted checks cover the final migration/stale-delivery changes; see VERIFICATION.md for precise results. [Load reproduction and reports](load/README.md) explain the local test limits. No public hosting, live provider delivery, real charge or bank transfer is claimed. CSV export is an instruction file, not a payment.
