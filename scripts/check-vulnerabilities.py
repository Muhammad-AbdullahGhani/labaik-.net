"""dotnet list reports findings with exit 0; turn findings into a failing CI check."""
import json
from pathlib import Path
import sys

raw = Path(sys.argv[1]).read_bytes()
# Windows PowerShell redirection uses UTF-16; CI/bash emits UTF-8.
report = json.loads(raw.decode("utf-16" if raw.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig"))
findings = []
for project in report["projects"]:
    for framework in project.get("frameworks", []):
        for group in ("topLevelPackages", "transitivePackages"):
            for package in framework.get(group, []):
                if package.get("vulnerabilities"):
                    findings.append(package["id"])
if report.get("logs"):
    # A feed error must not be mistaken for a clean report.
    if any(log.get("level", "").lower() == "error" for log in report["logs"]):
        sys.exit("Vulnerability scan could not complete.")
if findings:
    sys.exit("Vulnerable packages: " + ", ".join(sorted(set(findings))))
print("No known vulnerable packages reported, including transitive dependencies.")
