using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using PropertyApi.Infrastructure.Persistence;
using Xunit;

namespace PropertyApi.Architecture.Tests.Persistence;

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
            "  dotnet ef migrations add <Name> --project PropertyApi.Infrastructure " +
            "--startup-project PropertyApi --context AppDbContext\n\n" +
            "then review the generated Up/Down before committing.");
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
