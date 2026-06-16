param(
    [string]$SolutionPath = ".\PropertyApi.sln",
    [ValidateSet("Low", "Moderate", "High", "Critical")]
    [string]$FailureThreshold = "High"
)

$ErrorActionPreference = "Stop"

$severityRank = @{
    "Low"      = 1
    "Moderate" = 2
    "High"     = 3
    "Critical" = 4
}

$resolvedSolution = Resolve-Path -LiteralPath $SolutionPath -ErrorAction Stop
$solutionFullPath = $resolvedSolution.Path

Write-Host "Scanning NuGet vulnerabilities for $solutionFullPath. Failure threshold: $FailureThreshold and above."

$output = & dotnet list "$solutionFullPath" package --vulnerable --include-transitive 2>&1
$exitCode = $LASTEXITCODE

$output | ForEach-Object { Write-Host $_ }

if ($exitCode -ne 0) {
    Write-Error "NuGet vulnerability scan failed. The scan result cannot be trusted."
    exit 1
}

$thresholdValue = $severityRank[$FailureThreshold]
$foundBlockingVulnerability = $false

foreach ($line in $output) {
    if ($line -match "\b(Critical|High|Moderate|Low)\b") {
        $severity = $Matches[1]

        if ($severityRank[$severity] -ge $thresholdValue) {
            $foundBlockingVulnerability = $true
        }
    }
}

if ($foundBlockingVulnerability) {
    Write-Error "NuGet vulnerability scan found vulnerabilities with severity $FailureThreshold or higher."
    exit 1
}

Write-Host "No NuGet vulnerabilities found at or above threshold: $FailureThreshold."
exit 0
