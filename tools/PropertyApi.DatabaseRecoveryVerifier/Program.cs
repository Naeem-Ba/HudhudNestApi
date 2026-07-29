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
var result = new
{
    status = pendingMigrations.Length == 0 ? "PASS" : "FAIL",
    verifiedAtUtc = DateTimeOffset.UtcNow,
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
return pendingMigrations.Length == 0 ? 0 : 1;

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
