[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [string] $ScenarioPath = "ci/performance-scenarios.json",
    [string] $OutputDirectory = "artifacts/performance",
    [int] $WarmupRequests = 5,
    [int] $RequestsPerScenario = 50,
    [string] $AuthEmail = $env:PERF_AUTH_EMAIL,
    [string] $AuthPassword = $env:PERF_AUTH_PASSWORD
)

$ErrorActionPreference = "Stop"

Write-Warning "This script is a sequential developer diagnostic, not the release-blocking concurrent load suite. Use scripts/run-performance-tests.sh for release evidence."

Add-Type -AssemblyName System.Net.Http

if ($WarmupRequests -lt 0) {
    throw "WarmupRequests must be greater than or equal to zero."
}

if ($RequestsPerScenario -lt 1) {
    throw "RequestsPerScenario must be greater than zero."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$scenarioConfig = Get-Content -Path $ScenarioPath -Raw | ConvertFrom-Json
$base = $BaseUrl.TrimEnd("/")
$runId = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(30)

function ConvertTo-JsonContent {
    param([object] $Value)

    $json = $Value | ConvertTo-Json -Depth 12 -Compress
    return [System.Net.Http.StringContent]::new(
        $json,
        [System.Text.Encoding]::UTF8,
        "application/json")
}

function Expand-Template {
    param(
        [object] $Template,
        [hashtable] $Values
    )

    if ($null -eq $Template) {
        return $null
    }

    $json = $Template | ConvertTo-Json -Depth 12

    foreach ($key in $Values.Keys) {
        $placeholder = '${' + $key + '}'
        $json = $json.Replace($placeholder, [string] $Values[$key])
    }

    return $json | ConvertFrom-Json
}

function Get-ScenarioFlag {
    param(
        [object] $Scenario,
        [string] $Name
    )

    $property = $Scenario.PSObject.Properties[$Name]

    if ($null -eq $property) {
        return $false
    }

    return [bool] $property.Value
}

function Get-ScenarioInt {
    param(
        [object] $Scenario,
        [string] $Name,
        [int] $DefaultValue
    )

    $property = $Scenario.PSObject.Properties[$Name]

    if ($null -eq $property) {
        return $DefaultValue
    }

    return [int] $property.Value
}

function Invoke-ScenarioRequest {
    param(
        [string] $Method,
        [string] $Path,
        [object] $Body = $null,
        [string] $BearerToken = $null
    )

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        "$base$Path")

    if ($BearerToken) {
        $request.Headers.Authorization =
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new(
                "Bearer",
                $BearerToken)
    }

    if ($null -ne $Body) {
        $request.Content = ConvertTo-JsonContent $Body
    }

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $watch.Stop()

    [pscustomobject]@{
        statusCode = [int] $response.StatusCode
        elapsedMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 2)
        success = $response.IsSuccessStatusCode
        body = $content
    }
}

function Get-Percentile {
    param(
        [double[]] $Values,
        [double] $Percentile
    )

    if ($Values.Count -eq 0) {
        return 0
    }

    $sorted = $Values | Sort-Object
    $index = [Math]::Ceiling(($Percentile / 100.0) * $sorted.Count) - 1
    $index = [Math]::Max(0, [Math]::Min($index, $sorted.Count - 1))

    return [Math]::Round([double] $sorted[$index], 2)
}

function LoginForTokens {
    if ([string]::IsNullOrWhiteSpace($AuthEmail) -or
        [string]::IsNullOrWhiteSpace($AuthPassword)) {
        return $null
    }

    $response = Invoke-ScenarioRequest `
        -Method "POST" `
        -Path "/api/auth/login" `
        -Body @{
            Email = $AuthEmail
            Password = $AuthPassword
        }

    if ($response.statusCode -ne 200) {
        return $null
    }

    $json = $response.body | ConvertFrom-Json

    return [pscustomobject]@{
        accessToken = $json.accessToken
        refreshToken = $json.refreshToken
    }
}

$scenarioResults = New-Object System.Collections.Generic.List[object]

foreach ($scenario in $scenarioConfig.scenarios) {
    $requiresCredentials = Get-ScenarioFlag $scenario "requiresCredentials"
    $requiresRefreshToken = Get-ScenarioFlag $scenario "requiresRefreshToken"
    $scenarioWarmups = Get-ScenarioInt $scenario "warmupRequests" $WarmupRequests
    $scenarioRequests = Get-ScenarioInt $scenario "requestsPerScenario" $RequestsPerScenario

    if ($requiresCredentials -and
        ([string]::IsNullOrWhiteSpace($AuthEmail) -or
         [string]::IsNullOrWhiteSpace($AuthPassword))) {
        $scenarioResults.Add([pscustomobject]@{
            name = $scenario.name
            skipped = $true
            skipReason = "PERF_AUTH_EMAIL/PERF_AUTH_PASSWORD were not provided."
        })
        continue
    }

    $tokens = if ($requiresRefreshToken) { LoginForTokens } else { $null }

    if ($requiresRefreshToken -and -not $tokens) {
        $scenarioResults.Add([pscustomobject]@{
            name = $scenario.name
            skipped = $true
            skipReason = "Could not obtain a refresh token with the provided credentials."
        })
        continue
    }

    $templateValues = @{
        AUTH_EMAIL = $AuthEmail
        AUTH_PASSWORD = $AuthPassword
        REFRESH_TOKEN = if ($tokens) { $tokens.refreshToken } else { "" }
    }

    $body = Expand-Template `
        -Template $scenario.bodyTemplate `
        -Values $templateValues

    for ($i = 0; $i -lt $scenarioWarmups; $i++) {
        Invoke-ScenarioRequest `
            -Method $scenario.method `
            -Path $scenario.path `
            -Body $body | Out-Null
    }

    $samples = New-Object System.Collections.Generic.List[object]
    $scenarioWatch = [System.Diagnostics.Stopwatch]::StartNew()

    for ($i = 0; $i -lt $scenarioRequests; $i++) {
        $samples.Add((Invoke-ScenarioRequest `
            -Method $scenario.method `
            -Path $scenario.path `
            -Body $body))
    }

    $scenarioWatch.Stop()

    $latencies = @($samples | ForEach-Object { [double] $_.elapsedMs })
    $successCount = @($samples | Where-Object { $_.success }).Count
    $throughput =
        if ($scenarioWatch.Elapsed.TotalSeconds -le 0) {
            0
        }
        else {
            [Math]::Round($samples.Count / $scenarioWatch.Elapsed.TotalSeconds, 2)
        }

    $scenarioResults.Add([pscustomobject]@{
        name = $scenario.name
        method = $scenario.method
        path = $scenario.path
        skipped = $false
        requests = $samples.Count
        success = $successCount
        failed = $samples.Count - $successCount
        throughputRps = $throughput
        p50Ms = Get-Percentile $latencies 50
        p95Ms = Get-Percentile $latencies 95
        p99Ms = Get-Percentile $latencies 99
        minMs = [Math]::Round(($latencies | Measure-Object -Minimum).Minimum, 2)
        maxMs = [Math]::Round(($latencies | Measure-Object -Maximum).Maximum, 2)
        avgMs = [Math]::Round(($latencies | Measure-Object -Average).Average, 2)
    })
}

$result = [pscustomobject]@{
    runId = $runId
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    baseUrl = $base
    warmupRequests = $WarmupRequests
    requestsPerScenario = $RequestsPerScenario
    scenarioPath = $ScenarioPath
    results = $scenarioResults
}

$jsonPath = Join-Path $OutputDirectory "baseline-$runId.json"
$markdownPath = Join-Path $OutputDirectory "baseline-$runId.md"

$result | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding utf8

$markdown = New-Object System.Collections.Generic.List[string]
$markdown.Add("# Performance Baseline")
$markdown.Add("")
$markdown.Add("Run: $runId")
$markdown.Add("")
$markdown.Add("| Scenario | Requests | Success | Failed | RPS | p50 ms | p95 ms | p99 ms |")
$markdown.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")

foreach ($scenario in $scenarioResults) {
    if ($scenario.skipped) {
        $markdown.Add("| $($scenario.name) | skipped | - | - | - | - | - | - |")
        continue
    }

    $markdown.Add("| $($scenario.name) | $($scenario.requests) | $($scenario.success) | $($scenario.failed) | $($scenario.throughputRps) | $($scenario.p50Ms) | $($scenario.p95Ms) | $($scenario.p99Ms) |")
}

$markdown | Set-Content -Path $markdownPath -Encoding utf8

Get-Content $markdownPath
Write-Host "JSON: $jsonPath"
