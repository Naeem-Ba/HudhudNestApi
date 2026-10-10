using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using HudhudNestApi.Infrastructure.Persistence;
using Xunit;

namespace HudhudNestApi.Architecture.Tests.Persistence;

/// <summary>
/// Reproduces the process-global Npgsql configuration that Program.cs establishes at startup.
///
/// Program.cs sets Npgsql.EnableLegacyTimestampBehavior before anything builds a model. That
/// switch decides whether DateTime maps to "timestamp without time zone" or "timestamp with
/// time zone", so a model built without it differs from the shipped one on every CreatedAt,
/// UpdatedAt, and DeletedAt column in the schema. Comparing such a model against the migration
/// snapshot reports dozens of AlterColumn operations that do not exist.
///
/// This runs as a module initializer rather than a static constructor because Npgsql caches its
/// type mappings on first use: the switch has to be set before any other test in this assembly
/// touches the provider, not merely before this class is first used.
/// </summary>
internal static class NpgsqlModelCompatibility
{
    [ModuleInitializer]
    internal static void MatchProductionTimestampBehavior() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
}

/// <summary>
/// Guards against entity model changes that were never captured in a migration.
///
/// Without this, adding a property to an entity (and configuring it in AppDbContext) compiles,
/// passes every other test, and only fails at runtime as a Postgres 42703 "column does not
/// exist" — after deployment, on whichever endpoint happens to read that table first. That is
/// exactly how UserAccount.ProfileImagePublicId behaved: the property and its HasMaxLength
/// configuration shipped, the migration did not, and GET /api/properties returned 500 because
/// the generated SELECT named a column the table never had.
///
/// The check is offline: it compares the model against the compiled model snapshot, so it
/// needs no database, no connection string, and no container.
/// </summary>
[Trait("Category", "Persistence")]
public sealed class MigrationCompletenessTests
{
    /// <summary>
    /// The provider must match production (Npgsql), because provider-specific mappings are part
    /// of the model — a snapshot built for a different provider would compare unequal for
    /// reasons unrelated to a missing migration. The connection is never opened, so the string
    /// only needs to be syntactically valid.
    /// </summary>
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_diff_only;Username=none;Password=none")
            .Options;

        return new AppDbContext(options);
    }

    [Fact(DisplayName = "Every entity model change has a corresponding migration")]
    public void Model_Should_Have_No_Pending_Changes()
    {
        using var context = CreateContext();

        var pending = GetPendingOperations(context);

        Assert.True(
            pending.Count == 0,
            "The EF model no longer matches the last migration, so the database is missing " +
            "schema the code already queries.\n\n" +
            "Undeclared changes:\n" +
            string.Join("\n", pending.Select(op => "  - " + Describe(op))) +
            "\n\nGenerate the missing migration:\n" +
            "  dotnet ef migrations add <Name> --project HudhudNestApi.Infrastructure " +
            "--startup-project HudhudNestApi --context AppDbContext\n\n" +
            "then review the generated Up/Down before committing.");
    }

    /// <summary>
    /// EF Core 9+ compares the model with the snapshot before it migrates and refuses with
    /// PendingModelChangesWarning when they differ. The snapshot was generated with
    /// Npgsql.EnableLegacyTimestampBehavior on, so any process that calls MigrateAsync has to set
    /// the same switch first. The Migrator tool did not; on EF Core 10 it failed against a fresh
    /// database even though no model change was missing.
    /// </summary>
    [Fact(DisplayName = "Every tool that runs migrations sets the legacy timestamp switch first")]
    public void MigratingTools_Should_SetTheLegacyTimestampSwitch()
    {
        var toolsDirectory = Path.Combine(FindRepositoryRoot(), "tools");
        var programFiles = Directory.GetFiles(toolsDirectory, "Program.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        var migrating = programFiles
            .Where(path => File.ReadAllText(path).Contains("MigrateAsync(", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(migrating);

        var missing = migrating
            .Where(path => !File.ReadAllText(path).Contains("Npgsql.EnableLegacyTimestampBehavior", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(toolsDirectory, path))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These tools run MigrateAsync without AppContext.SetSwitch(\"Npgsql.EnableLegacyTimestampBehavior\", true), " +
            "so EF Core refuses to migrate (PendingModelChangesWarning): " + string.Join(", ", missing));
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

    /// <summary>
    /// Diffs the compiled model snapshot against the current model and returns the operations
    /// that would have to be applied. Empty means every model change is covered by a migration.
    /// </summary>
    private static IReadOnlyList<MigrationOperation> GetPendingOperations(AppDbContext context)
    {
        var differ = context.GetService<IMigrationsModelDiffer>();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;

        // No snapshot at all means no migrations exist yet — every table is "pending".
        if (snapshot is null)
        {
            return Array.Empty<MigrationOperation>();
        }

        var initializer = context.GetService<IModelRuntimeInitializer>();

        var snapshotModel = initializer.Initialize(
            ((IMutableModel)snapshot.Model).FinalizeModel(),
            designTime: true,
            validationLogger: null);

        var currentModel = context.GetService<IDesignTimeModel>().Model;

        return differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            currentModel.GetRelationalModel());
    }

    /// <summary>
    /// Turns a migration operation into something a reader can act on, rather than a type name.
    /// </summary>
    private static string Describe(MigrationOperation operation) => operation switch
    {
        AddColumnOperation op => $"AddColumn {op.Table}.{op.Name}",
        DropColumnOperation op => $"DropColumn {op.Table}.{op.Name}",
        AlterColumnOperation op => $"AlterColumn {op.Table}.{op.Name}",
        CreateTableOperation op => $"CreateTable {op.Name}",
        DropTableOperation op => $"DropTable {op.Name}",
        CreateIndexOperation op => $"CreateIndex {op.Table}.{op.Name}",
        DropIndexOperation op => $"DropIndex {op.Table}.{op.Name}",
        RenameColumnOperation op => $"RenameColumn {op.Table}.{op.Name} -> {op.NewName}",
        _ => operation.GetType().Name
    };
}
