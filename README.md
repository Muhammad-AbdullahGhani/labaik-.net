# Labaik Core

Step 1 only: .NET 10 health-only API and fake gateway, PostgreSQL, explicit empty migrations, Docker and CI. No booking, identity or payment endpoints exist yet.

- [Local run and manual deployment](docs/DEPLOYMENT.md)
- [Environment variable names](docs/ENVIRONMENT.md)
- [Actual verification and Docker blocker](docs/VERIFICATION.md)
- [Design](docs/DESIGN.md), [plan](docs/PLAN.md), [AI log](docs/AI_LOG.md)

After one-time `python scripts/create-local-env.py`, run `docker compose up --build --wait`. Requires a working Linux Docker daemon. Runtime startup never applies migrations; Compose starts separate migration tasks first.
