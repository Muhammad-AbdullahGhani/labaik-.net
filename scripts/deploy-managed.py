"""Optional host-neutral hook; no host is configured or implied by this skeleton."""
import json
import os
import sys
import urllib.request

url = os.environ["HOST_DEPLOY_URL"]
if not url.startswith("https://"):
    sys.exit("Deployment hook requires HTTPS.")
payload = json.dumps({"revision": os.environ["REVISION"], "images": {
    "api": f"{os.environ['IMAGE_ROOT']}/api:{os.environ['REVISION']}",
    "fake-gateway": f"{os.environ['IMAGE_ROOT']}/fake-gateway:{os.environ['REVISION']}"
}}).encode()
request = urllib.request.Request(url, data=payload, method="POST", headers={
    "Authorization": "Bearer " + os.environ["HOST_DEPLOY_TOKEN"], "Content-Type": "application/json"
})
try:
    with urllib.request.urlopen(request, timeout=300) as response:
        if response.status != 200:
            sys.exit("Deployment not confirmed complete; inspect the host separately.")
except Exception:
    # Avoid disclosing signed URLs, response bodies or credentials in CI output.
    sys.exit("Deployment request failed; inspect the host separately.")
print("Configured orchestrator reported deployment completed; inspect its health evidence.")
