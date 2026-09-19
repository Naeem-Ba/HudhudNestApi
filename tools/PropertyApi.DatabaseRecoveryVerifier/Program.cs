using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropertyApi.Infrastructure.Persistence;

var rawConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? Environment.GetEnvironmentVariable("RESTORE_TARGET_DATABASE_URL");
if (string.IsNullOrWhiteSpace(rawConnection))
{
    Console.Error.WriteLine("ConnectionStrings__DefaultConnection or RESTORE_TARGET_DATABASE_URL is required.");
    return 2;
}

var connectionString = NormalizeConnectionString(rawConnection);
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString)
    .Options;
await using var db = new AppDbContext(options);
// The backup restored here is a *pre-deploy* snapshot of Production, so it legitimately lacks
// any migration shipped in the release being gated. Requiring zero pending migrations on the
// raw restore made this gate fail on every release that adds a migration. Instead:
//   1. the restored history must contain no migration this build does not know (a restore
//      that is ahead of the code is a real problem and always fails);
//   2. pending migrations are applied to the disposable restored copy (forward-migration
//      rehearsal, opt-in and loopback-only so it can never touch a real database);
//   3. zero migrations must remain pending afterwards and the data checks must pass.
var knownMigrations = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
var appliedBefore = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
var unknownApplied = appliedBefore.Where(m => !knownMigrations.Contains(m)).ToArray();
var pendingBeforeRehearsal = (await db.Database.GetPendingMigrationsAsync()).ToArray();
var rehearsalRequested = string.Equals(
    Environment.GetEnvironmentVariable("RECOVERY_APPLY_PENDING_MIGRATIONS"), "true",
    StringComparison.OrdinalIgnoreCase);
var targetHost = new NpgsqlConnectionStringBuilder(connectionString).Host ?? string.Empty;
var targetIsLoopback = targetHost is "127.0.0.1" or "localhost" or "::1";
string? rehearsalError = null;
var rehearsalRan = false;
if (pendingBeforeRehearsal.Length > 0 && unknownApplied.Length == 0 && rehearsalRequested)
{
    if (!targetIsLoopback)
    {
        rehearsalError = "Refusing to apply migrations: target host is not loopback (disposable restore copies only).";
    }
    else
    {
        try
        {
            await db.Database.MigrateAsync();
            rehearsalRan = true;
        }
        catch (Exception exception)
        {
            rehearsalError = $"{exception.GetType().Name}: {exception.Message}";
        }
    }
}
var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToArray();
var checks = new Dictionary<string, long>
{
    ["Users"] = await db.Users.IgnoreQueryFilters().LongCountAsync(),
    ["UserAccounts"] = await db.UserAccounts.LongCountAsync(),
    ["Properties"] = await db.Properties.IgnoreQueryFilters().LongCountAsync(),
    ["PropertyImages"] = await db.PropertyImages.IgnoreQueryFilters().LongCountAsync(),
    ["Messages"] = await db.Messages.IgnoreQueryFilters().LongCountAsync(),
    ["RefreshTokens"] = await db.RefreshTokens.IgnoreQueryFilters().LongCountAsync(),
    ["Notifications"] = await db.Notifications.IgnoreQueryFilters().LongCountAsync(),
    ["AuditLogs"] = await db.AuditLogs.LongCountAsync(),
};
var postGis = await db.Database.SqlQueryRaw<string>("SELECT PostGIS_Lib_Version() AS \"Value\"").SingleAsync();
var verified = pendingMigrations.Length == 0 && unknownApplied.Length == 0 && rehearsalError is null;
var result = new
{
    status = verified ? "PASS" : "FAIL",
    verifiedAtUtc = DateTimeOffset.UtcNow,
    pendingMigrationsBeforeRehearsal = pendingBeforeRehearsal,
    forwardMigrationRehearsal = new { requested = rehearsalRequested, ran = rehearsalRan, error = rehearsalError },
    unknownAppliedMigrations = unknownApplied,
    pendingMigrations,
    entityRowCounts = checks,
    postGisVersion = postGis,
    sensitiveValuesMaterialized = false
};
var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
var outputPath = Environment.GetEnvironmentVariable("RECOVERY_VERIFICATION_OUTPUT")
    ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "database-recovery", "application-verification.json");
var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
if (!string.IsNullOrWhiteSpace(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}
await File.WriteAllTextAsync(outputPath, resultJson + Environment.NewLine);
Console.WriteLine(resultJson);
return verified ? 0 : 1;

static string NormalizeConnectionString(string value)
{
    value = value.Trim().Trim('"', '\'');
    if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return value;
    }
    var uri = new Uri(value);
    var credentials = uri.UserInfo.Split(':', 2);
    if (credentials.Length != 2)
    {
        throw new InvalidOperationException("PostgreSQL URI must include username and password.");
    }
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        Username = Uri.UnescapeDataString(credentials[0]),
        Password = Uri.UnescapeDataString(credentials[1])
    };
    return builder.ConnectionString;
}
