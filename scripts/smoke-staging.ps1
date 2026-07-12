[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaseUrl,
    [string] $Email = $env:SMOKE_EMAIL,
    [string] $Password = $env:SMOKE_PASSWORD
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')

function Invoke-SmokeRequest {
    param([string]$Method, [string]$Path, [object]$Body, [string]$Token)
    $headers = @{ Accept = 'application/json' }
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = "$BaseUrl$Path"; Headers = $headers }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = ($Body | ConvertTo-Json -Depth 10 -Compress)
    }
    Invoke-RestMethod @params
}

Write-Host '[1/5] Liveness'
Invoke-SmokeRequest GET '/health/live' $null $null | Out-Null

Write-Host '[2/5] Readiness'
Invoke-SmokeRequest GET '/health/ready' $null $null | Out-Null

Write-Host '[3/5] Public property list'
Invoke-SmokeRequest GET '/api/properties?page=1&pageSize=5' $null $null | Out-Null

if ($Email -and $Password) {
    Write-Host '[4/5] Login'
    $login = Invoke-SmokeRequest POST '/api/auth/login' @{ email = $Email; password = $Password } $null
    $token = if ($login.accessToken) { $login.accessToken } elseif ($login.token) { $login.token } else { $login.data.accessToken }
    if (-not $token) { throw 'No access token found in login response.' }

    Write-Host '[5/5] Authenticated profile'
    Invoke-SmokeRequest GET '/api/profile/me' $null $token | Out-Null
} else {
    Write-Warning 'Authenticated smoke skipped because credentials were not supplied.'
}

Write-Host 'Staging smoke tests passed.'
