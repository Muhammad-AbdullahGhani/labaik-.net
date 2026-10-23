"""Create local-only secrets once. No credentials are printed or committed."""
from pathlib import Path
import secrets

root = Path(__file__).resolve().parents[1]
# Exclusive creation avoids accidentally rotating credentials against an existing PostgreSQL volume.
with (root / ".env").open("x", encoding="utf-8") as output:
    for name in ("POSTGRES_PASSWORD", "APP_DB_PASSWORD", "GATEWAY_DB_PASSWORD",
                 "APP_MIGRATOR_PASSWORD", "GATEWAY_MIGRATOR_PASSWORD"):
        output.write(f"{name}={secrets.token_hex(32)}\n")
print("Created ignored .env; start with docker compose up --build --wait")
