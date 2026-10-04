using System.Globalization;
using System.Text.Json;

namespace HudhudNestApi.Integration.Tests.CI;

public sealed class CICDPipelineTests
{
    [Fact(DisplayName = "CI pipeline must run PostgreSQL with PostGIS and apply migrations")]
    public void CiPipeline_Should_Use_PostGis_And_Run_Migrations()
    {
        var repoRoot = FindRepositoryRoot();
        var ciFile = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");

        Assert.True(File.Exists(ciFile), "CI workflow file was not found.");

        var yaml = File.ReadAllText(ciFile);

        Assert.Contains("postgis/postgis", yaml);
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS postgis", yaml);
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", yaml);
        Assert.Contains("tools/HudhudNestApi.Migrator/HudhudNestApi.Migrator.csproj", yaml);
        Assert.Contains("dotnet test", yaml);
        Assert.Contains("ConnectionStrings__DefaultConnection", yaml);
        Assert.Contains("dotnet format", yaml);
        Assert.Contains("check-vulnerable-packages.ps1", yaml);
        Assert.Contains("VULNERABILITY_BASELINE_PATH", yaml);
        Assert.Contains("XPlat Code Coverage", yaml);
        Assert.Contains("check-coverage-baseline.ps1", yaml);
        Assert.Contains("dotnet tool restore", yaml);
        Assert.Contains("dotnet tool run reportgenerator", yaml);
        Assert.Contains("upload-artifact", yaml);
        Assert.Contains("ci-quality-gate-reports", yaml);
    }

    [Fact(DisplayName = "CI workflows must pin GitHub Actions to immutable SHAs")]
    public void CiWorkflows_Should_Pin_GitHub_Actions_To_Immutable_Shas()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowFiles = Directory.GetFiles(Path.Combine(repoRoot, ".github", "workflows"), "*.yml");
        var pinnedActionPattern = new System.Text.RegularExpressions.Regex(
            @"uses:\s+[^@\s]+@[0-9a-f]{40}\s+#\s+v",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var workflowFile in workflowFiles)
        {
            Assert.True(File.Exists(workflowFile), $"Workflow file was not found: {workflowFile}");

            var yaml = File.ReadAllText(workflowFile);

            Assert.DoesNotContain("uses: actions/setup-dotnet@v", yaml);
            Assert.DoesNotContain("uses: actions/upload-artifact@v", yaml);
            Assert.DoesNotContain("uses: actions/checkout@v", yaml);
            Assert.DoesNotContain("uses: actions/cache@v", yaml);
            Assert.DoesNotContain("@main", yaml);
            Assert.DoesNotContain("@master", yaml);
            Assert.DoesNotContain("@latest", yaml);
            Assert.True(pinnedActionPattern.IsMatch(yaml), $"Workflow does not contain pinned action references: {workflowFile}");
        }

        var ciYaml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "ci.yml"));
        var productionYaml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "production-gate.yml"));

        Assert.Contains("actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0", ciYaml);
        Assert.Contains("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1", ciYaml);
        Assert.Contains("actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0", productionYaml);
        Assert.Contains("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1", productionYaml);
    }

    [Fact(DisplayName = "CI quality gate scripts and baseline must be present")]
    public void CiPipeline_Should_Include_Quality_Gate_Scripts_And_Baseline()
    {
        var repoRoot = FindRepositoryRoot();

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "ci", "check-vulnerable-packages.ps1")),
            "Vulnerability gate script was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "ci", "check-coverage-baseline.ps1")),
            "Coverage baseline gate script was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "ci", "coverage-baseline.json")),
            "Coverage baseline file was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "ci", "vulnerability-baseline.json")),
            "Vulnerability baseline file was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "scripts", "verify-production-gate.ps1")),
            "Static production gate script was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "scripts", "smoke-staging.sh")),
            "Staging smoke test script was not found.");
    }

    [Fact(DisplayName = "Central Package Management must be enforced by CI")]
    public void CentralPackageManagement_Should_Be_Enforced_By_CI()
    {
        var repoRoot = FindRepositoryRoot();
        var centralPackagesFile = Path.Combine(repoRoot, "Directory.Packages.props");
        var packagePolicyFile = Path.Combine(repoRoot, "ci", "package-policy.json");
        var nugetConfigFile = Path.Combine(repoRoot, "NuGet.config");
        var directoryBuildProps = Path.Combine(repoRoot, "Directory.Build.props");

        Assert.True(File.Exists(centralPackagesFile), "Directory.Packages.props was not found.");
        Assert.True(File.Exists(packagePolicyFile), "Package policy file was not found.");
        Assert.True(File.Exists(nugetConfigFile), "NuGet.config was not found.");

        var centralPackages = File.ReadAllText(centralPackagesFile);
        var buildProps = File.ReadAllText(directoryBuildProps);
        var policy = File.ReadAllText(packagePolicyFile);
        var nugetConfig = File.ReadAllText(nugetConfigFile);

        Assert.Contains("<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>", centralPackages);
        Assert.Contains("<CentralPackageVersionOverrideEnabled>false</CentralPackageVersionOverrideEnabled>", centralPackages);
        Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", buildProps);
        Assert.Contains("\"requiredTestPackages\"", policy);
        Assert.Contains("\"Microsoft.NET.Test.Sdk\"", policy);
        Assert.Contains("\"xunit\"", policy);
        Assert.Contains("\"xunit.runner.visualstudio\"", policy);
        Assert.Contains("\"coverlet.collector\"", policy);
        Assert.Contains("https://api.nuget.org/v3/index.json", nugetConfig);

        var solutionProjects = GetSolutionProjectPaths(repoRoot);
        foreach (var projectFile in solutionProjects)
        {
            var projectXml = File.ReadAllText(projectFile);
            Assert.DoesNotContain("Version=\"", projectXml);
            Assert.DoesNotContain("<Version>", projectXml);

            if (projectXml.Contains("<PackageReference", StringComparison.Ordinal))
            {
                var lockFile = Path.Combine(Path.GetDirectoryName(projectFile)!, "packages.lock.json");
                Assert.True(File.Exists(lockFile), $"Lock file was not found for {projectFile}");
            }
        }
    }

    [Fact(DisplayName = "Supply-chain workflows must be active release gates")]
    public void SupplyChainGates_Should_Be_Consumed_By_Workflows()
    {
        var repoRoot = FindRepositoryRoot();
        var ciYaml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "ci.yml"));
        var productionYaml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "production-gate.yml"));
        var supplyChainYaml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "supply-chain-validation.yml"));

        Assert.Contains("validate-package-governance.ps1", ciYaml);
        Assert.Contains("validate-github-actions-pinning.py", ciYaml);
        Assert.Contains("validate-test-results.ps1", ciYaml);
        Assert.Contains("validate-coverage-thresholds.py", ciYaml);

        Assert.Contains("validate-package-governance.ps1", productionYaml);
        Assert.Contains("validate-github-actions-pinning.py", productionYaml);
        Assert.Contains("Capture API image digest", productionYaml);
        Assert.Contains("hudhudnest-dotnet.cdx.json", productionYaml);
        Assert.Contains("hudhudnest-container.spdx.json", productionYaml);
        Assert.Contains("container-scan.json", productionYaml);
        Assert.Contains("container-scan.sarif", productionYaml);
        Assert.Contains("generate-release-security-manifest.py", productionYaml);
        Assert.Contains("sbom-vulnerability-report.json", productionYaml);
        Assert.Contains("artifacts/supply-chain/**", productionYaml);

        Assert.Contains("validate-package-governance.ps1", supplyChainYaml);
        Assert.Contains("validate-github-actions-pinning.py", supplyChainYaml);
        Assert.Contains("dotnet restore \"${{ env.SOLUTION_PATH }}\" --locked-mode", supplyChainYaml);
        Assert.DoesNotContain("continue-on-error: true", ciYaml);
        Assert.DoesNotContain("continue-on-error: true", productionYaml);
        Assert.DoesNotContain("continue-on-error: true", supplyChainYaml);
    }

    [Fact(DisplayName = "Docker production gate must use locked restore and immutable image identity")]
    public void DockerProductionGate_Should_Use_Locked_Restore_And_Image_Identity()
    {
        var repoRoot = FindRepositoryRoot();
        var apiDockerfile = File.ReadAllText(Path.Combine(repoRoot, "HudhudNestApi", "Dockerfile"));
        var migratorDockerfile = File.ReadAllText(Path.Combine(repoRoot, "ci", "Dockerfile.migrator"));
        var compose = File.ReadAllText(Path.Combine(repoRoot, "ci", "docker-compose.production-gate.yml"));
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "production-gate.yml"));

        Assert.Contains("Directory.Packages.props", apiDockerfile);
        Assert.Contains("NuGet.config", apiDockerfile);
        Assert.Contains("--locked-mode", apiDockerfile);
        Assert.Contains(
            "mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled-extra AS runtime",
            apiDockerfile);
        Assert.Contains("USER $APP_UID", apiDockerfile);
        Assert.Contains("--locked-mode", migratorDockerfile);
        Assert.Contains("image: hudhudnest-api:${HUDHUDNEST_IMAGE_TAG:-production-gate}", compose);
        Assert.Contains("image: hudhudnest-migrator:${HUDHUDNEST_IMAGE_TAG:-production-gate}", compose);
        Assert.Contains("HUDHUDNEST_IMAGE_TAG: production-gate", workflow);
        Assert.Contains("hudhudnest-api:${HUDHUDNEST_IMAGE_TAG}", workflow);
    }

    [Fact(DisplayName = "Supply-chain security assets must be present")]
    public void SupplyChainSecurityAssets_Should_Be_Present()
    {
        var repoRoot = FindRepositoryRoot();
        var requiredFiles = new[]
        {
            Path.Combine(repoRoot, ".config", "dotnet-tools.json"),
            Path.Combine(repoRoot, ".github", "dependabot.yml"),
            Path.Combine(repoRoot, ".github", "workflows", "supply-chain-validation.yml"),
            Path.Combine(repoRoot, "ci", "coverage-thresholds.json"),
            Path.Combine(repoRoot, "ci", "package-policy.json"),
            Path.Combine(repoRoot, "ci", "vulnerability-exceptions.json"),
            Path.Combine(repoRoot, "scripts", "validate-package-governance.ps1"),
            Path.Combine(repoRoot, "scripts", "validate-github-actions-pinning.py"),
            Path.Combine(repoRoot, "scripts", "validate-coverage-thresholds.py"),
            Path.Combine(repoRoot, "scripts", "validate-test-results.ps1"),
            Path.Combine(repoRoot, "scripts", "generate-release-security-manifest.py"),
            Path.Combine(repoRoot, "docs", "security", "supply-chain-current-state.md"),
            Path.Combine(repoRoot, "docs", "security", "github-actions-pinning.md"),
            Path.Combine(repoRoot, "docs", "security", "supply-chain-runbook.md"),
            Path.Combine(repoRoot, "docs", "development", "central-package-management.md"),
            Path.Combine(repoRoot, "docs", "testing", "coverage-improvement-plan.md")
        };

        foreach (var file in requiredFiles)
        {
            Assert.True(File.Exists(file), $"Required supply-chain asset was not found: {file}");
        }
    }

    [Fact(DisplayName = "Vulnerability exception policy must be enforced, not merely declared")]
    public void VulnerabilityExceptionPolicy_Should_Be_Read_By_TheGate()
    {
        // vulnerability-exceptions.json declares exceptionsRequireOwner,
        // exceptionsRequireExpiration and exceptionsRequireRemediationIssue. Asserting
        // the file exists -- which the test above does -- says nothing about whether
        // anything acts on it, and for a long time nothing did: the baseline carried 17
        // suppressions with no owner, no date and no issue, and the gate passed every
        // build. This pins the gate to actually reading the policy and applying it.
        var repoRoot = FindRepositoryRoot();
        var gate = File.ReadAllText(Path.Combine(repoRoot, "ci", "check-vulnerable-packages.ps1"));

        Assert.Contains("vulnerability-exceptions.json", gate, StringComparison.Ordinal);

        foreach (var flag in new[]
                 {
                     "exceptionsRequireOwner",
                     "exceptionsRequireExpiration",
                     "exceptionsRequireRemediationIssue",
                     "wildcardExceptionsAllowed"
                 })
        {
            Assert.True(
                gate.Contains(flag, StringComparison.Ordinal),
                $"check-vulnerable-packages.ps1 does not act on the '{flag}' policy flag, " +
                "so vulnerability-exceptions.json is documentation rather than a gate.");
        }

        // A suppression whose advisory no longer appears in the scan has to be removed,
        // otherwise the baseline only ever grows and the gate reports "passed with
        // baseline" over a clean scan.
        Assert.Contains("staleAdvisories", gate, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Container vulnerability exceptions must expire and be owned")]
    public void ContainerVulnerabilityExceptions_Should_Be_Governed_By_TheManifestGate()
    {
        // The `container` list of vulnerability-exceptions.json is consumed only by
        // generate-release-security-manifest.py (check-vulnerable-packages.ps1 governs the NuGet
        // baseline). That script used to take every listed id as a permanent waiver, so an
        // exception kept hiding a HIGH/CRITICAL image finding long after its end date.
        var repoRoot = FindRepositoryRoot();
        var script = File.ReadAllText(
            Path.Combine(repoRoot, "scripts", "generate-release-security-manifest.py"));

        Assert.Contains("def accepted_container_exception_ids", script, StringComparison.Ordinal);
        Assert.Contains("expires < today", script, StringComparison.Ordinal);
        Assert.Contains("has no owner", script, StringComparison.Ordinal);
        Assert.Contains("has no remediationIssue", script, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Every accepted advisory must carry owner, expiry and remediation issue")]
    public void VulnerabilityBaseline_Entries_Should_Be_FullyGoverned()
    {
        var repoRoot = FindRepositoryRoot();
        var baselinePath = Path.Combine(repoRoot, "ci", "vulnerability-baseline.json");

        using var document = JsonDocument.Parse(File.ReadAllText(baselinePath));

        if (!document.RootElement.TryGetProperty("acceptedAdvisories", out var accepted))
        {
            Assert.Fail("vulnerability-baseline.json has no 'acceptedAdvisories' array.");
            return;
        }

        foreach (var entry in accepted.EnumerateArray())
        {
            var id = entry.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            Assert.False(string.IsNullOrWhiteSpace(id), "An accepted advisory has no 'id'.");

            foreach (var field in new[] { "package", "reason", "owner", "expiresOn", "remediationIssue" })
            {
                Assert.True(
                    entry.TryGetProperty(field, out var value) &&
                    !string.IsNullOrWhiteSpace(value.GetString()),
                    $"Accepted advisory '{id}' is missing '{field}'. Shipping a known " +
                    "vulnerability requires naming who owns it, when the exception ends, " +
                    "and where its removal is tracked.");
            }

            Assert.True(
                DateTime.TryParseExact(
                    entry.GetProperty("expiresOn").GetString(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var expiresOn),
                $"Accepted advisory '{id}' has an unparseable 'expiresOn'. Use YYYY-MM-DD.");

            Assert.True(
                expiresOn.Date >= DateTime.UtcNow.Date,
                $"Accepted advisory '{id}' expired on {expiresOn:yyyy-MM-dd}.");
        }
    }

    [Fact(DisplayName = "Production gate must fail fast for invalid staging base URL")]
    public void ProductionGate_Should_Fail_Fast_For_Invalid_Staging_Base_Url()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowFile = Path.Combine(repoRoot, ".github", "workflows", "production-gate.yml");
        var scriptFile = Path.Combine(repoRoot, "scripts", "smoke-staging.sh");

        Assert.True(File.Exists(workflowFile), "Production gate workflow file was not found.");
        Assert.True(File.Exists(scriptFile), "Staging smoke test script was not found.");

        var yaml = File.ReadAllText(workflowFile);
        var script = File.ReadAllText(scriptFile);

        Assert.Contains("bash scripts/smoke-staging.sh --validate-only", yaml);
        Assert.Contains("STAGING_BASE_URL must be an absolute HTTPS URL", script);
        Assert.Contains("example.com|*.example.com", script);
        Assert.Contains("STAGING_BASE_URL points to a placeholder or unsafe host", script);
        Assert.Contains("STAGING_BASE_URL resolves to the configured Production hostname", script);
    }

    [Fact(DisplayName = "Coverage gate must exclude generated migrations from Auth-sensitive coverage")]
    public void CoverageGate_Should_Exclude_Generated_Migrations_From_Auth_Sensitive_Coverage()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptFile = Path.Combine(repoRoot, "ci", "check-coverage-baseline.ps1");

        Assert.True(File.Exists(scriptFile), "Coverage baseline script was not found.");

        var script = File.ReadAllText(scriptFile);

        Assert.Contains("Test-IsGeneratedCoverageClass", script);
        Assert.Contains("[\\\\/](Migrations)[\\\\/]", script);
        Assert.Contains("\\.Designer\\.cs$", script);
        Assert.Contains("authGeneratedClassesExcluded", script);
    }

    [Fact(DisplayName = "Production gate must require Redis HA and failover validation")]
    public void ProductionGate_Should_Require_Redis_Ha_And_Failover_Validation()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowFile = Path.Combine(repoRoot, ".github", "workflows", "production-gate.yml");

        Assert.True(File.Exists(workflowFile), "Production gate workflow file was not found.");

        var yaml = File.ReadAllText(workflowFile);

        Assert.Contains("validate-redis-ha-topology", yaml);
        Assert.Contains("redis-staging-failover", yaml);
        Assert.Contains("REDIS_HA_EVIDENCE_JSON", yaml);
        Assert.Contains("REDIS_PROVIDER_FAILOVER_COMMAND", yaml);
        Assert.Contains("scripts/verify-redis-ha.sh", yaml);
        Assert.Contains("scripts/test-redis-failover.sh", yaml);
        Assert.Contains("scripts/verify-redis-recovery.sh", yaml);
        Assert.DoesNotContain("continue-on-error: true", yaml);
    }

    [Fact(DisplayName = "Redis resilience operational assets must be present")]
    public void RedisResilienceAssets_Should_Be_Present()
    {
        var repoRoot = FindRepositoryRoot();
        var requiredFiles = new[]
        {
            Path.Combine(repoRoot, "docs", "operations", "redis-current-state-assessment.md"),
            Path.Combine(repoRoot, "docs", "operations", "redis-failure-policy.md"),
            Path.Combine(repoRoot, "docs", "operations", "redis-ha-architecture.md"),
            Path.Combine(repoRoot, "docs", "operations", "redis-sla-slo.md"),
            Path.Combine(repoRoot, "docs", "operations", "redis-failure-and-failover-runbook.md"),
            Path.Combine(repoRoot, "docs", "operations", "redis-production-readiness-decision.md"),
            Path.Combine(repoRoot, "docs", "architecture", "adr-redis-workload-isolation.md"),
            Path.Combine(repoRoot, "docs", "security", "redis-security-controls.md"),
            Path.Combine(repoRoot, "scripts", "verify-redis-ha.sh"),
            Path.Combine(repoRoot, "scripts", "test-redis-failover.sh"),
            Path.Combine(repoRoot, "scripts", "verify-redis-recovery.sh"),
            Path.Combine(repoRoot, ".github", "workflows", "redis-ha-failover.yml"),
            Path.Combine(repoRoot, "observability", "dashboards", "hudhudnest-redis.json"),
            Path.Combine(repoRoot, "observability", "alerts", "hudhudnest-redis-alerts.yml")
        };

        foreach (var file in requiredFiles)
        {
            Assert.True(File.Exists(file), $"Required Redis resilience asset was not found: {file}");
        }
    }

    [Fact(DisplayName = "Staging smoke script must report failed endpoint details")]
    public void StagingSmokeScript_Should_Report_Failed_Endpoint_Details()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptFile = Path.Combine(repoRoot, "scripts", "smoke-staging.sh");

        Assert.True(File.Exists(scriptFile), "Staging smoke test script was not found.");

        var script = File.ReadAllText(scriptFile);

        Assert.Contains("failure_message=", script);
        Assert.Contains("echo \"::error::${failure_message}\"", script);
        Assert.Contains("staging-smoke-report.json", script);
        Assert.Contains("staging-smoke-junit.xml", script);
        Assert.Contains("staging-smoke-summary.md", script);
    }

    [Fact(DisplayName = "Production gate must fail closed and require complete Staging E2E")]
    public void ProductionGate_Should_Fail_Closed_And_Require_Complete_Staging_E2e()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowFile = Path.Combine(repoRoot, ".github", "workflows", "production-gate.yml");

        Assert.True(File.Exists(workflowFile), "Production gate workflow file was not found.");

        var yaml = File.ReadAllText(workflowFile);

        Assert.Contains("bash scripts/smoke-staging.sh --validate-only", yaml);
        Assert.Contains("STAGING_DEPLOY_HOOK_URL", yaml);
        Assert.Contains("/api/operational/build-info", yaml);
        Assert.Contains("EXPECTED_COMMIT_SHA", yaml);
        Assert.Contains("needs.staging-smoke.result == 'success'", yaml);
        Assert.Contains("needs.performance-validation.result == 'success'", yaml);
        Assert.Contains("deploy-production:", yaml);
        Assert.DoesNotContain("configured=false", yaml);
        Assert.DoesNotContain("continue-on-error: true", yaml);
    }

    [Fact(DisplayName = "Performance gate must use two instances and block on query and concurrency evidence")]
    public void PerformanceGate_Should_Be_Concurrent_MultiInstance_And_FailClosed()
    {
        var repoRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            repoRoot, ".github", "workflows", "performance-validation.yml"));
        var runner = File.ReadAllText(Path.Combine(repoRoot, "scripts", "run-performance-tests.sh"));
        var compose = File.ReadAllText(Path.Combine(
            repoRoot, "performance", "docker-compose.performance.yml"));

        Assert.Contains("run-performance-tests.sh", workflow);
        Assert.Contains("if-no-files-found: error", workflow);
        Assert.DoesNotContain("continue-on-error", workflow);
        Assert.Contains("api1:", compose);
        Assert.Contains("api2:", compose);
        Assert.Contains("shared_preload_libraries=pg_stat_statements", compose);
        Assert.Contains("capture-pg-stat-statements.sh", runner);
        Assert.Contains("capture-query-plans.sh", runner);
        Assert.Contains("PERF_REQUIRE_APPROVED_BUDGETS", runner);
    }

    [Fact(DisplayName = "Staging smoke script must run lifecycle tests and require reports")]
    public void StagingSmokeScript_Should_Run_Lifecycle_Tests_And_Require_Reports()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptFile = Path.Combine(repoRoot, "scripts", "smoke-staging.sh");

        Assert.True(File.Exists(scriptFile), "Staging smoke test script was not found.");

        var script = File.ReadAllText(scriptFile);

        Assert.Contains("set -Eeuo pipefail", script);
        Assert.Contains("HudhudNestApi.StagingSmokeTests.csproj", script);
        Assert.Contains("staging-smoke-report.json", script);
        Assert.Contains("staging-smoke-junit.xml", script);
        Assert.Contains("staging-smoke-summary.md", script);
        Assert.Contains("A mandatory journey is failed or skipped", script);
        Assert.Contains("STAGING_SMOKE_CLEANUP_SECRET", script);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HudhudNestApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing HudhudNestApi.sln.");
    }

    private static IReadOnlyCollection<string> GetSolutionProjectPaths(string repoRoot)
    {
        var solutionFile = Path.Combine(repoRoot, "HudhudNestApi.sln");
        var projectPaths = new List<string>();

        foreach (var line in File.ReadAllLines(solutionFile))
        {
            const string marker = ".csproj\"";
            if (!line.Contains(marker, StringComparison.Ordinal))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 2)
                continue;

            var relativePath = parts[1].Trim().Trim('"').Replace('\\', Path.DirectorySeparatorChar);
            projectPaths.Add(Path.GetFullPath(Path.Combine(repoRoot, relativePath)));
        }

        return projectPaths;
    }
}
