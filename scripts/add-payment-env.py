"""Add missing local payment secrets without rotating existing database/auth credentials."""
from pathlib import Path
import base64
import secrets

path = Path(__file__).resolve().parents[1] / ".env"
existing = path.read_text(encoding="utf-8")
names = {line.split("=", 1)[0].strip() for line in existing.splitlines() if "=" in line}
new_values = {
    "GATEWAY_API_KEY": secrets.token_hex(32),
    "WEBHOOK_ENCRYPTION_KEY": base64.b64encode(secrets.token_bytes(32)).decode(),
    "WEBHOOK_SIGNATURE_KEY": base64.b64encode(secrets.token_bytes(32)).decode(),
}
with path.open("a", encoding="utf-8") as output:
    if existing and not existing.endswith("\n"):
        output.write("\n")
    for name, value in new_values.items():
        if name not in names:
            output.write(f"{name}={value}\n")
print("Missing payment secrets added to ignored .env; existing entries preserved. No values printed.")
