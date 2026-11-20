import json
from datetime import datetime, timedelta, timezone

# Sample script for QuotaWidget command provider
# Outputs Generic Quota JSON (version 1) to stdout

now = datetime.now(timezone.utc)
five_hour_reset = (now + timedelta(hours=3)).strftime("%Y-%m-%dT%H:%M:%SZ")

payload = {
    "version": 1,
    "families": [
        {
            "name": "Pro plan",
            "windows": [
                {
                    "name": "5h",
                    "used_percent": 42.0,
                    "resets_at": five_hour_reset,
                    "estimated": False
                },
                {
                    "name": "Week",
                    "used_percent": 61.5,
                    "resets_at": None,
                    "estimated": True
                }
            ]
        }
    ],
    "error": None
}

print(json.dumps(payload, indent=2))
