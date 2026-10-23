# Labaik Core

Steps 1, 2a, 2b and 3: .NET 10 API, domain state machine/SAR pricing/voucher rules, PostgreSQL constraints/versioning and Firebase-to-local JWT/rotating refresh identity. Fake gateway now simulates authenticated checkout/payment/query/redirect. Core public booking/payment/offer/payout routes remain disabled until their safeguards are complete.

- [Local run and manual deployment](docs/DEPLOYMENT.md)
- [Environment variable names and auth setup](docs/ENVIRONMENT.md)
- [Actual verification](docs/VERIFICATION.md)
- [Plain-language demo preparation](docs/DEMO_PREP.md)
- [Design](docs/DESIGN.md), [plan](docs/PLAN.md), [AI log](docs/AI_LOG.md)

After one-time `python scripts/create-local-env.py`, run `docker compose up --build --wait`. Requires Linux Docker. Existing .env files are preserved; see identity setup for adding auth configuration. Runtime startup never applies migrations: Compose runs separate tasks first. Without Firebase/auth settings, health works and authentication fails closed. No production fake verifier exists.

Step 3 adds durable idempotent payment attempts, authenticated encrypted webhook inbox, gateway re-query, atomic capture/outbox and lease-based workers. Only the 28 new payment tests were run this turn; older full-suite results are historical. See VERIFICATION.md for exact evidence. Existing local env files can add missing payment secrets with `python scripts/add-payment-env.py` without rotating database passwords.
