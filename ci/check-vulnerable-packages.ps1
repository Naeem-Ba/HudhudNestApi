[CmdletBinding()]
param(
    [string] $SolutionPath = "PropertyApi.sln",
    [string] $BaselinePath = "ci/vulnerability-baseline.json",
    [string] $PolicyPath = "ci/vulnerability-exceptions.json",
    [string] $OutputDirectory = "artifacts/vulnerability"
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$resolvedSolutionPath = (Resolve-Path $SolutionPath).Path

$reportPath = Join-Path $OutputDirectory "vulnerable-packages.txt"
$summaryPath = Join-Path $OutputDirectory "vulnerability-summary.md"

function Write-Summary {
    param([string[]] $Lines)
    $Lines | Set-Content -Path $summaryPath -Encoding utf8
    Get-Content $summaryPath
}

# ---------------------------------------------------------------------------
# 1. Baseline governance.
#
# This runs BEFORE the scan result is examined, and its outcome does not depend
# on whether anything vulnerable was found. That ordering is the point: the old
# script returned early on a clean scan and never looked at the baseline at all,
# which is how 17 suppressions -- every one of them already fixed -- survived in
# the file while the gate reported "passed with baseline" over a clean scan.
# ---------------------------------------------------------------------------

$policy = [pscustomobject]@{
    exceptionsRequireOwner            = $true
    exceptionsRequireExpiration       = $true
    exceptionsRequireRemediationIssue = $true
    wildcardExceptionsAllowed         = $false
}

if (Test-Path $PolicyPath) {
    $declared = (Get-Content -Path $PolicyPath -Raw | ConvertFrom-Json).policy
    if ($null -ne $declared) { $policy = $declared }
}
else {
    Write-Host "::warning::Exception policy file not found at $PolicyPath. Falling back to the strictest defaults."
}

$baselineEntries = @()

if (Test-Path $BaselinePath) {
    $baseline = Get-Content -Path $BaselinePath -Raw | ConvertFrom-Json
    $baselineEntries = @($baseline.acceptedAdvisories) | Where-Object { $null -ne $_ }
}

$governanceFailures = @()
$today = (Get-Date).Date

foreach ($entry in $baselineEntries) {
    $id = [string] $entry.id
    $label = if ([string]::IsNullOrWhiteSpace($id)) { "<entry with no id>" } else { $id }

    if ([string]::IsNullOrWhiteSpace($id)) {
        $governanceFailures += "An accepted advisory has no 'id'."
        continue
    }

    if ([string]::IsNullOrWhiteSpace([string] $entry.package)) {
        $governanceFailures += "$label is missing 'package'."
    }

    if ([string]::IsNullOrWhiteSpace([string] $entry.reason)) {
        $governanceFailures += "$label is missing 'reason'."
    }

    if (-not $policy.wildcardExceptionsAllowed) {
        if ($id -like "*``**" -or ([string] $entry.package) -like "*``**") {
            $governanceFailures += "$label uses a wildcard, which the policy forbids. Suppress one advisory for one package."
        }
    }

    if ($policy.exceptionsRequireOwner -and [string]::IsNullOrWhiteSpace([string] $entry.owner)) {
        $governanceFailures += "$label is missing 'owner'. Name the person answerable for removing it."
    }

    if ($policy.exceptionsRequireRemediationIssue -and [string]::IsNullOrWhiteSpace([string] $entry.remediationIssue)) {
        $governanceFailures += "$label is missing 'remediationIssue'. Link the issue tracking its removal."
    }

    if ($policy.exceptionsRequireExpiration) {
        $rawExpiry = [string] $entry.expiresOn
        [datetime] $expiresOn = [datetime]::MinValue

        if ([string]::IsNullOrWhiteSpace($rawExpiry)) {
            $governanceFailures += "$label is missing 'expiresOn'. An exception without an end date is a permanent one."
        }
        elseif (-not [datetime]::TryParseExact(
                $rawExpiry,
                'yyyy-MM-dd',
                [cultureinfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::None,
                [ref] $expiresOn)) {
            $governanceFailures += "$label has an unparseable 'expiresOn' value '$rawExpiry'. Use YYYY-MM-DD."
        }
        elseif ($expiresOn.Date -lt $today) {
            $governanceFailures += "$label expired on $rawExpiry. Fix the advisory or renew the exception with a new date and rationale."
        }
    }
}

if ($governanceFailures.Count -gt 0) {
    Write-Summary (@(
        "# Vulnerability Gate"
        ""
        "Status: failed (baseline governance)"
        ""
        "Every accepted advisory must name an owner, an expiry date, and the issue"
        "tracking its removal. These entries do not:"
        ""
    ) + ($governanceFailures | ForEach-Object { "- $_" }) + @(
        ""
        "Policy: $PolicyPath"
        "Baseline: $BaselinePath"
    ))

    exit 1
}

# ---------------------------------------------------------------------------
# 2. Scan.
# ---------------------------------------------------------------------------

$output = & dotnet list $resolvedSolutionPath package --vulnerable --include-transitive 2>&1
$exitCode = $LASTEXITCODE

$output | Set-Content -Path $reportPath -Encoding utf8
$outputText = $output -join [Environment]::NewLine

if ($exitCode -ne 0) {
    Write-Summary @(
        "# Vulnerability Gate"
        ""
        "Status: failed"
        ""
        "The dotnet vulnerable-package scan command failed."
    )

    Get-Content $reportPath
    exit $exitCode
}

$detectedAdvisories = [regex]::Matches(
    $outputText,
    "GHSA-[0-9a-z]{4}-[0-9a-z]{4}-[0-9a-z]{4}",
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase) |
    ForEach-Object { $_.Value.ToLowerInvariant() } |
    Sort-Object -Unique

$acceptedAdvisories = @($baselineEntries |
    ForEach-Object { ([string] $_.id).ToLowerInvariant() } |
    Sort-Object -Unique)

$newAdvisories = @($detectedAdvisories | Where-Object { $_ -notin $acceptedAdvisories })

# A suppression for something the scan no longer reports is dead weight, and dead
# weight is what the gate was built out of. Failing on it forces the file to shrink
# as fixes land, instead of growing forever.
$staleAdvisories = @($acceptedAdvisories | Where-Object { $_ -notin $detectedAdvisories })

$failures = @()

if ($newAdvisories.Count -gt 0) {
    $failures += @("New advisories not covered by the baseline:") +
        ($newAdvisories | ForEach-Object { "- $_" }) + ""
}

if ($staleAdvisories.Count -gt 0) {
    $failures += @("Baseline entries the scan no longer reports -- the fix has landed, remove them:") +
        ($staleAdvisories | ForEach-Object { "- $_" }) + ""
}

if ($failures.Count -gt 0) {
    Write-Summary (@(
        "# Vulnerability Gate"
        ""
        "Status: failed"
        ""
    ) + $failures + @(
        "See vulnerable-packages.txt for the full report."
        "Upgrade the dependency, or record an exception in $BaselinePath with an owner,"
        "an expiry date, and a remediation issue."
    ))

    Get-Content $reportPath
    exit 1
}

if ($detectedAdvisories.Count -gt 0) {
    Write-Summary (@(
        "# Vulnerability Gate"
        ""
        "Status: passed with baseline"
        ""
        "Every detected advisory has a governed exception recorded in the baseline."
        ""
        "Baseline: $BaselinePath"
        ""
        "Accepted advisories:"
    ) + ($detectedAdvisories | ForEach-Object { "- $_" }))

    exit 0
}

Write-Summary @(
    "# Vulnerability Gate"
    ""
    "Status: passed"
    ""
    "No vulnerable NuGet packages were reported, and the baseline is empty."
)
