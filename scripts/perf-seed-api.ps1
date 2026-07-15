[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [string] $Email = "perf.seed@example.test",
    [string] $Password = "PerfSeed123!",
    [int] $PropertyCount = 250,
    [string] $City = "Damascus",
    [string] $CountryCode = "SY",
    [string] $OutputDirectory = "artifacts/performance"
)

$ErrorActionPreference = "Stop"

if ($PropertyCount -lt 0) {
    throw "PropertyCount must be greater than or equal to zero."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$base = $BaseUrl.TrimEnd("/")
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

function Invoke-ApiJson {
    param(
        [string] $Method,
        [string] $Path,
        [object] $Body = $null,
        [string] $BearerToken = $null,
        [switch] $AllowFailure
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

    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

    if (-not $response.IsSuccessStatusCode -and -not $AllowFailure) {
        throw "HTTP $([int] $response.StatusCode) $($response.ReasonPhrase) from $Method $Path. Body: $content"
    }

    [pscustomobject]@{
        StatusCode = [int] $response.StatusCode
        Body = $content
        Json = if ([string]::IsNullOrWhiteSpace($content)) { $null } else { $content | ConvertFrom-Json }
    }
}

function Login {
    $response = Invoke-ApiJson `
        -Method "POST" `
        -Path "/api/auth/login" `
        -Body @{
            Email = $Email
            Password = $Password
        } `
        -AllowFailure

    if ($response.StatusCode -eq 200) {
        return $response.Json.accessToken
    }

    return $null
}

$accessToken = Login

if (-not $accessToken) {
    Invoke-ApiJson `
        -Method "POST" `
        -Path "/api/auth/register" `
        -Body @{
            FirstName = "Perf"
            LastName = "Seed"
            Email = $Email
            Password = $Password
        } | Out-Null

    $accessToken = Login
}

if (-not $accessToken) {
    throw "Could not login or register the performance seed user."
}

$created = New-Object System.Collections.Generic.List[string]

for ($index = 1; $index -le $PropertyCount; $index++) {
    $listingType = if ($index % 5 -eq 0) { "ForSale" } else { "ForRent" }
    $isSale = $listingType -eq "ForSale"
    $latitude = 33.513800 + (($index % 50) * 0.001)
    $longitude = 36.276500 + (($index % 50) * 0.001)

    $body = @{
        OwnerId = "00000000-0000-0000-0000-000000000000"
        Title = "Perf Seed Property $index"
        Description = "Repeatable performance seed property $index."
        ListingType = $listingType
        Street = "Performance Street $index"
        City = $City
        Region = "Damascus"
        CountryCode = $CountryCode
        PostalCode = "00000"
        Latitude = [Math]::Round($latitude, 6)
        Longitude = [Math]::Round($longitude, 6)
        ColdRent = if ($isSale) { $null } else { 150000 + ($index * 100) }
        WarmRent = if ($isSale) { $null } else { 170000 + ($index * 100) }
        PurchasePrice = if ($isSale) { 250000000 + ($index * 10000) } else { $null }
        Deposit = if ($isSale) { $null } else { 300000 }
        AdditionalCosts = if ($isSale) { $null } else { 20000 }
        CurrencyCode = "SYP"
        Rooms = 1 + ($index % 5)
        Area = 45 + ($index % 140)
        Floor = $index % 12
        HasBalcony = ($index % 2 -eq 0)
        HasElevator = ($index % 3 -eq 0)
        HasParkingSpace = ($index % 4 -eq 0)
        HeatingType = "Gas"
        AvailableFrom = [DateTime]::UtcNow.ToString("O")
        AmenityIds = @()
    }

    $response = Invoke-ApiJson `
        -Method "POST" `
        -Path "/api/properties" `
        -Body $body `
        -BearerToken $accessToken

    if ($response.Json.id) {
        $created.Add([string] $response.Json.id)
    }
}

$manifest = [pscustomobject]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    baseUrl = $base
    email = $Email
    requestedPropertyCount = $PropertyCount
    createdPropertyCount = $created.Count
    city = $City
    countryCode = $CountryCode
    propertyIds = $created
}

$manifestPath = Join-Path $OutputDirectory "seed-manifest.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8

Write-Host "Performance seed completed. Created $($created.Count) properties."
Write-Host "Manifest: $manifestPath"
