# Labaik Core

Steps 1, 2a and 2b: .NET 10 API, domain state machine/SAR pricing/voucher rules, PostgreSQL constraints/versioning and Firebase-to-local JWT/rotating refresh identity. Gateway remains health-only; booking/payment/offers/payout endpoints are disabled.

- [Local run and manual deployment](docs/DEPLOYMENT.md)
- [Environment variable names and auth setup](docs/ENVIRONMENT.md)
- [Actual verification](docs/VERIFICATION.md)
- [Design](docs/DESIGN.md), [plan](docs/PLAN.md), [AI log](docs/AI_LOG.md)

After one-time `python scripts/create-local-env.py`, run `docker compose up --build --wait`. Requires Linux Docker. Existing .env files are preserved; see identity setup for adding auth configuration. Runtime startup never applies migrations: Compose runs separate tasks first. Without Firebase/auth settings, health works and authentication fails closed. No production fake verifier exists.
