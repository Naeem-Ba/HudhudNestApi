[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path ".").Path,
    [string] $SolutionPath = "PropertyApi.sln",
    [string] $PolicyPath = "ci/package-policy.json",
    [string] $OutputDirectory = "artifacts/supply-chain"
)

$ErrorActionPreference = "Stop"

$RepositoryRoot = (Resolve-Path $RepositoryRoot).Path
$resolvedSolutionPath = Join-Path $RepositoryRoot $SolutionPath
$resolvedPolicyPath = Join-Path $RepositoryRoot $PolicyPath
$outputPath = Join-Path (Join-Path $RepositoryRoot $OutputDirectory) "package-governance-report.json"

New-Item -ItemType Directory -Force -Path (Split-Path $outputPath -Parent) | Out-Null

if (-not (Test-Path $resolvedSolutionPath)) {
    throw "Solution file was not found: $resolvedSolutionPath"
}

if (-not (Test-Path $resolvedPolicyPath)) {
    throw "Package policy file was not found: $resolvedPolicyPath"
}

$policy = Get-Content -Path $resolvedPolicyPath -Raw | ConvertFrom-Json
$failures = New-Object System.Collections.Generic.List[string]

function Convert-ToRepoPath {
    param([string] $Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    $trimCharacters = @(
        [char] [System.IO.Path]::DirectorySeparatorChar,
        [char] [System.IO.Path]::AltDirectorySeparatorChar
    )
    $rootUri = New-Object System.Uri (($RepositoryRoot.TrimEnd($trimCharacters)) + [System.IO.Path]::DirectorySeparatorChar)
    $pathUri = New-Object System.Uri $full
    $relative = [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
    return ($relative -replace "\\", "/")
}

function Test-FloatingOrRangeVersion {
    param([string] $Version)

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return $true
    }

    return $Version -match "\*" -or $Version -match "^[\[\(].*[\]\)]$" -or $Version -match ","
}

function Test-PreviewVersion {
    param([string] $Version)

    return $Version -match "-(alpha|beta|preview|rc|nightly|dev|ci|pre)"
}

$directoryPackagesPath = Join-Path $RepositoryRoot "Directory.Packages.props"
if (-not (Test-Path $directoryPackagesPath)) {
    $failures.Add("Directory.Packages.props is missing.")
    [xml] $centralXml = "<Project />"
}
else {
    [xml] $centralXml = Get-Content -Path $directoryPackagesPath -Raw
}

$manageCentrally = $centralXml.Project.PropertyGroup.ManagePackageVersionsCentrally
if ($manageCentrally -ne "true") {
    $failures.Add("ManagePackageVersionsCentrally must be true in Directory.Packages.props.")
}

$versionOverrideEnabled = $centralXml.Project.PropertyGroup.CentralPackageVersionOverrideEnabled
if ($versionOverrideEnabled -ne "false") {
    $failures.Add("CentralPackageVersionOverrideEnabled must be false.")
}

$centralVersions = @{}
$centralPackageReports = New-Object System.Collections.Generic.List[object]

foreach ($packageVersion in @(Select-Xml -Xml $centralXml -XPath "//PackageVersion" | ForEach-Object { $_.Node })) {
    $packageName = $packageVersion.GetAttribute("Include")
    $version = $packageVersion.GetAttribute("Version")

    if ([string]::IsNullOrWhiteSpace($packageName)) {
        $failures.Add("A PackageVersion entry is missing Include.")
        continue
    }

    if ($centralVersions.ContainsKey($packageName)) {
        $failures.Add("Duplicate PackageVersion entry found for $packageName.")
    }

    $centralVersions[$packageName] = $version

    $packageFailures = @()
    if (Test-FloatingOrRangeVersion $version) {
        $packageFailures += "Central version is floating, ranged, or empty."
    }

    if ((Test-PreviewVersion $version) -and -not ($packageName -in @($policy.allowedPreviewPackages))) {
        $packageFailures += "Preview package is not allowlisted."
    }

    foreach ($packageFailure in $packageFailures) {
        $failures.Add("Directory.Packages.props: $packageName $version - $packageFailure")
    }

    $centralPackageReports.Add([pscustomobject]@{
        package = $packageName
        version = $version
        result = $(if ($packageFailures.Count -eq 0) { "passed" } else { "failed" })
        failures = $packageFailures
    })
}

$solutionText = Get-Content -Path $resolvedSolutionPath -Raw
$solutionProjectPaths = [regex]::Matches($solutionText, 'Project\("\{[^"]+\}"\)\s=\s"[^"]+",\s"([^"]+\.csproj)"') |
    ForEach-Object {
        [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot ($_.Groups[1].Value -replace "\\", [System.IO.Path]::DirectorySeparatorChar)))
    } |
    Sort-Object -Unique

$solutionProjectSet = @{}
foreach ($projectPath in $solutionProjectPaths) {
    $solutionProjectSet[$projectPath.ToLowerInvariant()] = $true
}

$projectFiles = Get-ChildItem -Path $RepositoryRoot -Filter "*.csproj" -Recurse -File |
    Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
    Sort-Object FullName

$projectReports = New-Object System.Collections.Generic.List[object]
$packageReferenceReports = New-Object System.Collections.Generic.List[object]

foreach ($projectFile in $projectFiles) {
    [xml] $projectXml = Get-Content -Path $projectFile.FullName -Raw
    $relativeProjectPath = Convert-ToRepoPath $projectFile.FullName
    $isSolutionProject = $solutionProjectSet.ContainsKey($projectFile.FullName.ToLowerInvariant())
    $projectFailures = New-Object System.Collections.Generic.List[string]
    $packageReferences = @(Select-Xml -Xml $projectXml -XPath "//PackageReference" | ForEach-Object { $_.Node })

    foreach ($packageReference in $packageReferences) {
        $packageName = $packageReference.GetAttribute("Include")
        $versionAttribute = $packageReference.GetAttribute("Version")
        $versionElement = $packageReference.SelectSingleNode("Version")
        $versionOverrideAttribute = $packageReference.GetAttribute("VersionOverride")
        $versionOverrideElement = $packageReference.SelectSingleNode("VersionOverride")
        $packageFailures = New-Object System.Collections.Generic.List[string]

        if (-not $centralVersions.ContainsKey($packageName)) {
            $packageFailures.Add("PackageReference is missing a central PackageVersion.")
        }

        if (-not [string]::IsNullOrWhiteSpace($versionAttribute) -or $null -ne $versionElement) {
            $packageFailures.Add("PackageReference must not specify Version when CPM is enabled.")
        }

        if (-not [string]::IsNullOrWhiteSpace($versionOverrideAttribute) -or $null -ne $versionOverrideElement) {
            if (-not ($packageName -in @($policy.allowedVersionOverrides))) {
                $packageFailures.Add("VersionOverride is not allowlisted.")
            }
        }

        foreach ($packageFailure in $packageFailures) {
            $message = "${relativeProjectPath}: ${packageName}: ${packageFailure}"
            $projectFailures.Add($message)
            $failures.Add($message)
        }

        $packageReferenceReports.Add([pscustomobject]@{
            project = $relativeProjectPath
            package = $packageName
            requestedVersion = $(if ($centralVersions.ContainsKey($packageName)) { $centralVersions[$packageName] } else { $null })
            resolvedVersion = $(if ($centralVersions.ContainsKey($packageName)) { $centralVersions[$packageName] } else { $null })
            source = "nuget.org"
            direct = $true
            transitive = $false
            result = $(if ($packageFailures.Count -eq 0) { "passed" } else { "failed" })
            failures = @($packageFailures)
        })
    }

    if ($isSolutionProject -and $packageReferences.Count -gt 0 -and $policy.restore.lockFilesRequiredForSolutionProjects) {
        $lockPath = Join-Path $projectFile.DirectoryName "packages.lock.json"
        if (-not (Test-Path $lockPath)) {
            $message = "${relativeProjectPath}: packages.lock.json is required for locked restore."
            $projectFailures.Add($message)
            $failures.Add($message)
        }
    }

    $projectReports.Add([pscustomobject]@{
        project = $relativeProjectPath
        inSolution = $isSolutionProject
        packageReferenceCount = $packageReferences.Count
        result = $(if ($projectFailures.Count -eq 0) { "passed" } else { "failed" })
        failures = @($projectFailures)
    })
}

foreach ($expectedTestPackage in $policy.testPackages.PSObject.Properties) {
    $packageName = $expectedTestPackage.Name
    $expectedVersion = [string] $expectedTestPackage.Value

    if (-not $centralVersions.ContainsKey($packageName)) {
        $failures.Add("Required test package $packageName is missing from Directory.Packages.props.")
        continue
    }

    if ($centralVersions[$packageName] -ne $expectedVersion) {
        $failures.Add("Test package $packageName must be $expectedVersion but is $($centralVersions[$packageName]).")
    }
}

$nugetConfigPath = Join-Path $RepositoryRoot "NuGet.config"
$sourceReports = New-Object System.Collections.Generic.List[object]

if (-not (Test-Path $nugetConfigPath)) {
    $failures.Add("NuGet.config is missing.")
}
else {
    [xml] $nugetConfig = Get-Content -Path $nugetConfigPath -Raw
    $sourceNodes = @(Select-Xml -Xml $nugetConfig -XPath "//packageSources/add" | ForEach-Object { $_.Node })
    $approvedSources = @{}

    foreach ($source in @($policy.nugetSources.approvedSources)) {
        $approvedSources[[string] $source.name] = [string] $source.url
    }

    foreach ($sourceNode in $sourceNodes) {
        $name = $sourceNode.GetAttribute("key")
        $url = $sourceNode.GetAttribute("value")
        $sourceFailures = New-Object System.Collections.Generic.List[string]

        if (-not $approvedSources.ContainsKey($name) -or $approvedSources[$name] -ne $url) {
            $sourceFailures.Add("NuGet source is not approved by ci/package-policy.json.")
        }

        if ($policy.nugetSources.requireHttps -and $url -notmatch "^https://") {
            $sourceFailures.Add("NuGet source must use HTTPS.")
        }

        foreach ($sourceFailure in $sourceFailures) {
            $failures.Add("NuGet.config: ${name}: ${sourceFailure}")
        }

        $sourceReports.Add([pscustomobject]@{
            name = $name
            url = $url
            result = $(if ($sourceFailures.Count -eq 0) { "passed" } else { "failed" })
            failures = @($sourceFailures)
        })
    }

    if ((Select-Xml -Xml $nugetConfig -XPath "//packageSourceCredentials" | Select-Object -First 1) -and -not $policy.nugetSources.allowCredentialsInRepository) {
        $failures.Add("NuGet.config must not contain packageSourceCredentials.")
    }
}

$overallStatus = "failed"
if ($failures.Count -eq 0) {
    $overallStatus = "passed"
}

$centralPackageReportArray = @($centralPackageReports.ToArray())
$projectReportArray = @($projectReports.ToArray())
$packageReferenceReportArray = @($packageReferenceReports.ToArray())
$sourceReportArray = @($sourceReports.ToArray())
$failureArray = @($failures.ToArray())

$report = [pscustomobject]@{
    schemaVersion = 1
    status = $overallStatus
    centralPackageManagementEnabled = ($manageCentrally -eq "true")
    centralPackageCount = $centralVersions.Count
    solutionProjectCount = $solutionProjectSet.Count
    scannedProjectCount = $projectFiles.Count
    packageReferenceCount = $packageReferenceReports.Count
    lockFilesRequiredForSolutionProjects = [bool] $policy.restore.lockFilesRequiredForSolutionProjects
    packageVersions = $centralPackageReportArray
    projects = $projectReportArray
    packageReferences = $packageReferenceReportArray
    packageSources = $sourceReportArray
    failures = $failureArray
}

$report | ConvertTo-Json -Depth 8 | Set-Content -Path $outputPath -Encoding utf8

Write-Host "Package governance report: $outputPath"
Write-Host "Status: $($report.status)"

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Host "::error::$failure"
    }

    exit 1
}
