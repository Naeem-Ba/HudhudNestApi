namespace PropertyApi.Architecture.Tests.ProductionHardening;

public sealed class DatabaseRecoveryAssetsTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Theory]
    [InlineData("scripts/database/backup-postgres.sh")]
    [InlineData("scripts/database/verify-backup.sh")]
    [InlineData("scripts/database/restore-postgres.sh")]
    [InlineData("scripts/database/verify-restored-database.sh")]
    [InlineData("scripts/database/apply-retention.sh")]
    [InlineData("scripts/deployment/rollback-application.sh")]
    [InlineData(".github/workflows/database-backup.yml")]
    [InlineData(".github/workflows/database-restore-drill.yml")]
    [InlineData(".github/workflows/rollback-production.yml")]
    public void Required_recovery_asset_must_exist(string relativePath)
    {
        Assert.True(File.Exists(Path.Combine(Root, relativePath)), $"Missing recovery asset: {relativePath}");
    }

    [Fact]
    public void Restore_script_must_have_disposable_target_and_production_guards()
    {
        var text = Read("scripts/database/restore-postgres.sh");
        Assert.Contains("RESTORE_TARGET_DISPOSABLE", text);
        Assert.Contains("ALLOW_DESTRUCTIVE_RESTORE", text);
        Assert.Contains("RESTORE_TARGET_ENVIRONMENT", text);
        Assert.Contains("PRODUCTION_DATABASE_URL", text);
        Assert.Contains("restore_drill", text);
        Assert.Contains("--exit-on-error", text);
    }

    [Fact]
    public void Backup_must_be_custom_format_encrypted_and_verified_before_upload()
    {
        var text = Read("scripts/database/backup-postgres.sh");
        Assert.Contains("--format=custom", text);
        Assert.Contains("AES256", text);
        Assert.Contains("pg_restore --list", text);
        Assert.True(text.IndexOf("verify-backup.sh", StringComparison.Ordinal) < text.IndexOf("upload_file", StringComparison.Ordinal));
    }

    [Fact]
    public void Critical_table_counts_must_be_complete_and_compared_during_restore()
    {
        var backup = Read("scripts/database/backup-postgres.sh");
        var verification = Read("scripts/database/verify-restored-database.sh");

        Assert.Contains("Required critical table is missing from the source database", backup);
        Assert.Contains("Critical table row-count manifest is incomplete", backup);
        Assert.Contains("Backup manifest has no row count for required table", verification);
        Assert.Contains("Row-count mismatch", verification);
    }

    [Fact]
    public void Recovery_workflows_must_not_ignore_failures_or_publish_database_archives()
    {
        foreach (var path in new[] { ".github/workflows/database-backup.yml", ".github/workflows/database-restore-drill.yml" })
        {
            var text = Read(path);
            Assert.DoesNotContain("continue-on-error", text);
            Assert.DoesNotContain("artifacts/database-recovery/*.dump", text);
        }
    }

    [Fact]
    public void Production_gate_must_require_actual_database_recovery()
    {
        var text = Read(".github/workflows/production-gate.yml");
        Assert.Contains("database-recovery:", text);
        Assert.Contains("database-restore-drill.yml", text);
        Assert.Contains("RECOVERY_RESULT", text);
        Assert.Contains("[ \"${RECOVERY_RESULT}\" = \"success\" ]", text);
    }

    [Fact]
    public void Recovery_report_must_require_application_and_database_evidence()
    {
        var text = Read("scripts/database/create-recovery-report.sh");
        var workflow = Read(".github/workflows/database-restore-drill.yml");

        Assert.Contains("database-verification.json", text);
        Assert.Contains("application-verification.json", text);
        Assert.Contains("Required recovery evidence did not pass", text);
        Assert.Contains("create-recovery-report.sh", workflow);
        Assert.DoesNotContain("DatabaseRecoveryVerifier.csproj --configuration Release --no-build --no-restore |", workflow);
    }

    [Fact]
    public void Database_rollback_must_not_be_part_of_application_rollback()
    {
        var text = Read("scripts/deployment/rollback-application.sh");
        Assert.DoesNotContain("dotnet ef database update", text);
        Assert.DoesNotContain("Database.Migrate", text);
        Assert.Contains("databaseDowngradePerformed:false", text);
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PropertyApi.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("PropertyApi repository root not found.");
    }
}
