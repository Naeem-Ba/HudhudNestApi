using Npgsql;

namespace HudhudNestApi.Configuration;

/// <summary>
/// The mirror image of <see cref="StagingEnvironmentGuard"/>. Staging's guard proves Staging
/// points at Staging resources; nothing proved the reverse, so a Production service whose
/// DATABASE_URL / Redis / storage settings had been copy-pasted from Staging (or a Staging-only
/// switch left on) would boot happily and write real users' data into test infrastructure.
///
/// Deliberately narrow: it only rejects settings that are unambiguously Staging-shaped (the
/// literal marker "staging" in a resource name, or a Staging-only switch being on). A guard
/// that guessed too broadly could stop a healthy Production service from booting.
/// </summary>
public static class ProductionEnvironmentGuard
{
    private const string StagingMarker = "staging";

    public static void Validate(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsProduction())
            return;

        var problems = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuration["Staging:EnvironmentId"]))
            problems.Add("Staging:EnvironmentId is set (Staging-only setting).");

        if (configuration.GetValue<bool>("Staging:TestSupport:Enabled"))
            problems.Add("Staging:TestSupport:Enabled is true (fixed OTP / cleanup endpoints are Staging-only).");

        var databaseName = TryGetDatabaseName(
            Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? configuration.GetConnectionString("DefaultConnection"));
        if (Mentions(databaseName))
            problems.Add("The database name contains 'staging'.");

        var redis = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"]
            ?? configuration["REDIS_CONNECTION_STRING"]
            ?? configuration["REDIS_URL"];
        if (Mentions(redis))
            problems.Add("The Redis connection references a 'staging' host.");

        if (Mentions(configuration["Cloudinary:FolderPrefix"]))
            problems.Add("Cloudinary:FolderPrefix is a Staging folder.");

        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Production refuses to start with Staging-shaped configuration: " +
                string.Join(" ", problems) +
                " Check that this service was not given Staging's environment variables.");
    }

    private static bool Mentions(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(StagingMarker, StringComparison.OrdinalIgnoreCase);

    // Only the database *name* is inspected -- a host or user that merely contains the word
    // must not be able to block boot. Unparseable input is ignored here: the real resolver
    // (PostgresConnectionStringResolver) already fails on it with a precise message.
    private static string? TryGetDatabaseName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim().Trim('"', '\'');
        try
        {
            if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(new Uri(value).AbsolutePath.TrimStart('/'));

            return new NpgsqlConnectionStringBuilder(value).Database;
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException or FormatException)
        {
            return null;
        }
    }
}
