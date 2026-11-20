<#
.SYNOPSIS
    Wrapper script for worker commands with QuotaWidget quota management.

.DESCRIPTION
    Demonstrates the orchestrator pattern:
    1. Optionally picks the best provider matching a worker tag.
    2. Executes the worker command.
    3. Refreshes the quota via qw refresh.

.EXAMPLE
    # Pick the best provider for Gemini, then run work and refresh:
    $id = qw pick "agy:gemini-*"
    .\worker-wrapper.ps1 -ProviderId $id -Command "agy" -Arguments @("-p", "Summarize task")

.EXAMPLE
    # Direct usage with an explicit provider ID:
    .\worker-wrapper.ps1 -ProviderId "claude" -Command "claude" -Arguments @("-p", "Review code")
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$WorkerTag,

    [Parameter(Mandatory = $false)]
    [string]$ProviderId,

    [Parameter(Mandatory = $true)]
    [string]$Command,

    [Parameter(Mandatory = $false)]
    [string[]]$Arguments = @()
)

# Step 1: Pick provider if worker tag was provided and ProviderId was not
if (-not $ProviderId -and $WorkerTag) {
    Write-Host "[QuotaWidget] Picking provider for worker tag '$WorkerTag'..."
    $ProviderId = qw pick $WorkerTag
    if ($LASTEXITCODE -ne 0 -or -not $ProviderId) {
        Write-Error "[QuotaWidget] Failed to pick qualifying provider for '$WorkerTag' (exit code: $LASTEXITCODE)"
        exit 3
    }
    Write-Host "[QuotaWidget] Selected provider: $ProviderId"
}

if (-not $ProviderId) {
    Write-Error "[QuotaWidget] Either -ProviderId or -WorkerTag must be specified."
    exit 1
}

# Step 2: Execute the worker command
Write-Host "[QuotaWidget] Running worker: $Command $($Arguments -join ' ')"
& $Command @Arguments
$workerExitCode = $LASTEXITCODE

# Step 3: Trigger refresh for the provider
Write-Host "[QuotaWidget] Refreshing quota for provider '$ProviderId'..."
qw refresh $ProviderId

exit $workerExitCode
