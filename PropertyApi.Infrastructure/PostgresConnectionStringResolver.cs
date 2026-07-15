using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace PropertyApi.Infrastructure;

internal static class PostgresConnectionStringResolver
{
    public static string Resolve(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var rawConnectionString = environment.IsProduction()
            ? Environment.GetEnvironmentVariable("DATABASE_URL")
            : configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            throw new InvalidOperationException(
                environment.IsProduction()
                    ? "DATABASE_URL environment variable is required in Production."
                    : "ConnectionStrings:DefaultConnection is missing in appsettings.json.");
        }

        return NormalizePostgresConnectionString(rawConnectionString, environment);
    }

    private static string NormalizePostgresConnectionString(
        string rawConnectionString,
        IHostEnvironment environment)
    {
        var value = rawConnectionString.Trim().Trim('"', '\'');

        NpgsqlConnectionStringBuilder builder;

        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            builder = ConvertPostgresUriToNpgsqlBuilder(value);
        }
        else
        {
            builder = new NpgsqlConnectionStringBuilder(value);
        }

        ApplyDefaultPostgresSettings(builder, environment);

        if (environment.IsProduction())
        {
            ValidateProductionDatabaseConnection(builder);
        }

        return builder.ConnectionString;
    }

    private static NpgsqlConnectionStringBuilder ConvertPostgresUriToNpgsqlBuilder(
        string postgresUri)
    {
        var uri = new Uri(postgresUri);
        var userInfoParts = uri.UserInfo.Split(':', 2);

        if (userInfoParts.Length != 2)
        {
            throw new InvalidOperationException(
                "Postgres URI must contain both username and password.");
        }

        var username = Uri.UnescapeDataString(userInfoParts[0]);
        var password = Uri.UnescapeDataString(userInfoParts[1]);
        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = string.IsNullOrWhiteSpace(database) ? "postgres" : database,
            Username = username,
            Password = password
        };

        var query = uri.Query.TrimStart('?');

        if (!string.IsNullOrWhiteSpace(query))
        {
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var keyValue = part.Split('=', 2);
                if (keyValue.Length != 2)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(keyValue[0]);
                var value = Uri.UnescapeDataString(keyValue[1]);

                if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                {
                    builder.SslMode = value.Equals("require", StringComparison.OrdinalIgnoreCase)
                        ? SslMode.Require
                        : Enum.Parse<SslMode>(value, ignoreCase: true);
                }
            }
        }

        return builder;
    }

    private static void ApplyDefaultPostgresSettings(
        NpgsqlConnectionStringBuilder builder,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var isSupabasePooler =
            !string.IsNullOrWhiteSpace(builder.Host) &&
            builder.Host.Contains(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase);

        if (builder.Port <= 0)
        {
            builder.Port = 5432;
        }

        builder.Pooling = true;

        if (isSupabasePooler && builder.MaxPoolSize > 20)
        {
            builder.MaxPoolSize = 20;
        }

        if (builder.MaxPoolSize <= 0)
        {
            builder.MaxPoolSize = isSupabasePooler ? 20 : 50;
        }

        if (builder.Timeout <= 0)
        {
            builder.Timeout = 30;
        }

        if (builder.CommandTimeout <= 0)
        {
            builder.CommandTimeout = 60;
        }

        if (environment.IsProduction())
        {
            builder.SslMode = SslMode.Require;
        }
    }

    private static void ValidateProductionDatabaseConnection(
        NpgsqlConnectionStringBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Host))
        {
            throw new InvalidOperationException("Production database host is required.");
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException("Production database name is required.");
        }

        if (string.IsNullOrWhiteSpace(builder.Username))
        {
            throw new InvalidOperationException("Production database username is required.");
        }

        if (string.IsNullOrWhiteSpace(builder.Password))
        {
            throw new InvalidOperationException("Production database password is required.");
        }

        if (HasEnabledTrustServerCertificate(builder))
        {
            throw new InvalidOperationException(
                "Production database connection must not contain Trust Server Certificate=true. Use SSL Mode=Require or SSL Mode=VerifyFull without trusting invalid server certificates.");
        }

        if (builder.Password.Contains("[YOUR-PASSWORD]", StringComparison.OrdinalIgnoreCase) ||
            builder.Password.Contains("YOUR_SUPABASE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Production database password still contains a placeholder value.");
        }

        var isSupabasePooler =
            builder.Host.Contains(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase);

        var isSupabaseDirect =
            builder.Host.StartsWith("db.", StringComparison.OrdinalIgnoreCase) &&
            builder.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase);

        if (isSupabasePooler)
        {
            if (!builder.Username.Contains('.', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Supabase Pooler username must be in the format '<db-user>.<project-ref>', for example 'postgres.wevfsshyxmydfiifjzka'.");
            }

            var usernameParts = builder.Username.Split('.', 2);

            if (usernameParts.Length != 2 ||
                string.IsNullOrWhiteSpace(usernameParts[0]) ||
                string.IsNullOrWhiteSpace(usernameParts[1]))
            {
                throw new InvalidOperationException(
                    "Invalid Supabase Pooler username. Expected '<db-user>.<project-ref>'.");
            }

            if (builder.Port != 5432 && builder.Port != 6543)
            {
                throw new InvalidOperationException(
                    "Supabase Pooler port must be 5432 for Session mode or 6543 for Transaction mode.");
            }
        }

        if (isSupabaseDirect)
        {
            if (builder.Username.Contains('.', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Supabase direct database connection usually uses username 'postgres', not 'postgres.<project-ref>'. The '<project-ref>' format is for Pooler.");
            }

            if (builder.Port != 5432)
            {
                throw new InvalidOperationException(
                    "Supabase direct database connection should use port 5432.");
            }
        }

        if (builder.SslMode != SslMode.Require &&
            builder.SslMode != SslMode.VerifyFull)
        {
            throw new InvalidOperationException(
                "Production database connection must use SSL Mode=Require or SSL Mode=VerifyFull.");
        }
    }

    private static bool HasEnabledTrustServerCertificate(
        NpgsqlConnectionStringBuilder builder)
    {
        var hasSpacedAlias =
            builder.TryGetValue("Trust Server Certificate", out var spacedAliasValue) &&
            IsEnabledBooleanConnectionStringValue(spacedAliasValue);

        var hasCompactAlias =
            builder.TryGetValue("TrustServerCertificate", out var compactAliasValue) &&
            IsEnabledBooleanConnectionStringValue(compactAliasValue);

        return hasSpacedAlias || hasCompactAlias;
    }

    private static bool IsEnabledBooleanConnectionStringValue(object? value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            string stringValue => bool.TryParse(stringValue, out var boolValue) && boolValue,
            _ => false
        };
    }
}
