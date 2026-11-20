# Sample script for QuotaWidget command provider
# Outputs Generic Quota JSON (version 1) to stdout

$fiveHourReset = (Get-Date).ToUniversalTime().AddHours(3).ToString("yyyy-MM-ddTHH:mm:ssZ")

$payload = [ordered]@{
    version = 1
    families = @(
        [ordered]@{
            name = "Pro plan"
            windows = @(
                [ordered]@{
                    name = "5h"
                    used_percent = 42.0
                    resets_at = $fiveHourReset
                    estimated = $false
                },
                [ordered]@{
                    name = "Week"
                    used_percent = 61.5
                    resets_at = $null
                    estimated = $true
                }
            )
        }
    )
    error = $null
}

$payload | ConvertTo-Json -Depth 5
