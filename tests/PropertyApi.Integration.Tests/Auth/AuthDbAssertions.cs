using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.Auth;

internal static class AuthDbAssertions
{
    public static async Task<int> UserCountByEmailAsync(
        PostgresAuthTestFactory factory,
        string email)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                var normalized =
                    email.Trim().ToUpperInvariant();

                return await db.Users
                    .AsNoTracking()
                    .CountAsync(
                        user =>
                            user.NormalizedEmail ==
                            normalized);
            });

    public static async Task<int> UserCountByPhoneAsync(
        PostgresAuthTestFactory factory,
        string phoneNumber)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                var hasher =
                    services.GetRequiredService<
                        IPhoneNumberLookupHasher>();

                var hash =
                    hasher.Compute(phoneNumber);

                return await db.Users
                    .AsNoTracking()
                    .CountAsync(
                        user =>
                            user.PhoneNumberLookupHash ==
                            hash);
            });

    public static async Task<
        (Guid UserId, bool PhoneConfirmed)?>
        UserByPhoneAsync(
            PostgresAuthTestFactory factory,
            string phoneNumber)
        => await factory.InScopeAsync<
            (Guid UserId, bool PhoneConfirmed)?>(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                var hasher =
                    services.GetRequiredService<
                        IPhoneNumberLookupHasher>();

                var hash =
                    hasher.Compute(phoneNumber);

                var user =
                    await db.Users
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            user =>
                                user.PhoneNumberLookupHash ==
                                hash);

                return user is null
                    ? null
                    : (
                        UserId: user.Id,
                        PhoneConfirmed:
                            user.PhoneNumberConfirmed);
            });

    public static async Task<int> UserAccountCountAsync(
        PostgresAuthTestFactory factory,
        Guid userId)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserAccounts
                    .AsNoTracking()
                    .CountAsync(
                        account =>
                            account.Id == userId);
            });

    public static async Task<int>
        UserAccountTotalCountAsync(
            PostgresAuthTestFactory factory)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserAccounts
                    .AsNoTracking()
                    .CountAsync();
            });

    public static async Task<int> RoleCountAsync(
        PostgresAuthTestFactory factory,
        Guid userId)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserRoles
                    .AsNoTracking()
                    .CountAsync(
                        role =>
                            role.UserId == userId);
            });

    public static async Task<int> LoginCountAsync(
        PostgresAuthTestFactory factory,
        string provider,
        string providerId)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserLogins
                    .AsNoTracking()
                    .CountAsync(
                        login =>
                            login.LoginProvider ==
                            provider &&
                            login.ProviderKey ==
                            providerId);
            });

    public static async Task<OtpState>
        LatestOtpStateAsync(
            PostgresAuthTestFactory factory,
            string phoneNumber)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                var entity =
                    FindOtpEntity(db);

                var table =
                    entity.GetTableName()
                    ?? throw new InvalidOperationException(
                        "OTP entity has no table mapping.");

                var schema =
                    entity.GetSchema();

                var store =
                    StoreObjectIdentifier.Table(
                        table,
                        schema);

                var phoneProperty =
                    FindProperty(
                        entity,
                        "PhoneNumber",
                        "Phone");

                var usedProperty =
                    FindProperty(
                        entity,
                        "IsUsed",
                        "Used");

                var attemptsProperty =
                    FindProperty(
                        entity,
                        "AttemptCount",
                        "Attempts");

                var createdProperty =
                    entity.FindProperty("CreatedAt")
                    ?? entity.FindProperty(
                        "CreatedUtc");

                var phoneColumn =
                    phoneProperty.GetColumnName(
                        store)
                    ?? throw new InvalidOperationException(
                        "OTP phone column mapping was not found.");

                var usedColumn =
                    usedProperty.GetColumnName(
                        store)
                    ?? throw new InvalidOperationException(
                        "OTP used column mapping was not found.");

                var attemptsColumn =
                    attemptsProperty.GetColumnName(
                        store)
                    ?? throw new InvalidOperationException(
                        "OTP attempts column mapping was not found.");

                var orderColumn =
                    createdProperty?.GetColumnName(
                        store);

                var sql =
                    $"SELECT {Q(usedColumn)}, " +
                    $"{Q(attemptsColumn)} " +
                    $"FROM {QuoteTable(schema, table)} " +
                    $"WHERE {Q(phoneColumn)} = @phone " +
                    (
                        orderColumn is null
                            ? string.Empty
                            : $"ORDER BY {Q(orderColumn)} DESC "
                    ) +
                    "LIMIT 1;";

                var connection =
                    db.Database.GetDbConnection();

                await EnsureOpenAsync(connection);

                await using var command =
                    connection.CreateCommand();

                command.CommandText = sql;

                var parameter =
                    command.CreateParameter();

                parameter.ParameterName =
                    "phone";

                parameter.Value =
                    phoneNumber;

                command.Parameters.Add(
                    parameter);

                await using var reader =
                    await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    throw new InvalidOperationException(
                        $"No OTP row exists for phone '{phoneNumber}'.");
                }

                return new OtpState(
                    IsUsed:
                        reader.GetBoolean(0),

                    AttemptCount:
                        Convert.ToInt32(
                            reader.GetValue(1)));
            });

    private static IEntityType FindOtpEntity(
        AppDbContext db)
        => db.Model
               .GetEntityTypes()
               .FirstOrDefault(
                   entity =>
                       entity.ClrType.Name.Contains(
                           "Otp",
                           StringComparison.OrdinalIgnoreCase)
                       &&
                       (
                           entity.FindProperty(
                               "IsUsed") is not null
                           ||
                           entity.FindProperty(
                               "Used") is not null
                       )
                       &&
                       (
                           entity.FindProperty(
                               "AttemptCount") is not null
                           ||
                           entity.FindProperty(
                               "Attempts") is not null
                       ))
           ?? throw new InvalidOperationException(
               "Could not locate the OTP entity in AppDbContext metadata.");

    private static IProperty FindProperty(
        IEntityType entity,
        params string[] names)
    {
        foreach (var name in names)
        {
            var property =
                entity.FindProperty(name);

            if (property is not null)
            {
                return property;
            }
        }

        throw new InvalidOperationException(
            $"Could not find any of " +
            $"[{string.Join(", ", names)}] " +
            $"on OTP entity " +
            $"'{entity.ClrType.FullName}'.");
    }

    private static async Task EnsureOpenAsync(
        DbConnection connection)
    {
        if (connection.State !=
            System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }

    private static string Q(
        string identifier)
        => $"\"{identifier.Replace(
            "\"",
            "\"\"")}\"";

    private static string QuoteTable(
        string? schema,
        string table)
        => string.IsNullOrWhiteSpace(schema)
            ? Q(table)
            : $"{Q(schema)}.{Q(table)}";
}

internal sealed record OtpState(
    bool IsUsed,
    int AttemptCount);