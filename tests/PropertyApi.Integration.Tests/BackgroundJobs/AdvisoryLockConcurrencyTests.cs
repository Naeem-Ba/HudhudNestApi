using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PropertyApi.Infrastructure.Persistence;
using Xunit;

namespace PropertyApi.Integration.Tests.BackgroundJobs;

/// <summary>
/// Exercises BackgroundJobLock against a real PostgreSQL server — advisory locks are a
/// server-side feature with no in-memory equivalent, so this cannot be a plain unit test.
/// Mirrors PostgresAuthTestFactory's connection-string convention rather than spinning up a
/// full WebApplicationFactory, since this only needs a raw connection, not the app.
/// </summary>
[Collection("AuthPostgres")]
public sealed class AdvisoryLockConcurrencyTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        ?? throw new InvalidOperationException(
            "A PostgreSQL test connection string is required. Set " +
            "TEST_POSTGRES_CONNECTION_STRING or ConnectionStrings__DefaultConnection.");

    [Theory]
    [InlineData(BackgroundJobLockKeys.ListingExpiry)]
    [InlineData(BackgroundJobLockKeys.SavedSearchMatch)]
    [InlineData(BackgroundJobLockKeys.PhoneVerificationReminder)]
    [InlineData(BackgroundJobLockKeys.OtpCleanup)]
    public async Task TryRunAsync_SkipsTheAction_WhenAnotherConnectionAlreadyHoldsTheKey(long key)
    {
        // Simulates a second application instance already mid-sweep: acquire the same key on
        // a separate connection before the code under test ever runs.
        await using var holder = new NpgsqlConnection(ConnectionString);
        await holder.OpenAsync();
        Assert.True(await TryAcquireAsync(holder, key), "Test setup could not simulate another instance holding the lock.");

        try
        {
            await using var contender = new NpgsqlConnection(ConnectionString);
            await contender.OpenAsync();

            var actionRan = false;
            var didRun = await BackgroundJobLock.TryRunAsync(
                contender,
                key,
                "test-job",
                NullLogger.Instance,
                () =>
                {
                    actionRan = true;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            Assert.False(didRun);
            Assert.False(actionRan);
        }
        finally
        {
            await ReleaseAsync(holder, key);
        }
    }

    [Fact]
    public async Task TryRunAsync_RunsTheActionAndReleasesTheLock_WhenNoOtherConnectionHoldsIt()
    {
        const long key = BackgroundJobLockKeys.OtpCleanup;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var actionRan = false;
        var didRun = await BackgroundJobLock.TryRunAsync(
            connection,
            key,
            "test-job",
            NullLogger.Instance,
            () =>
            {
                actionRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(didRun);
        Assert.True(actionRan);

        // If TryRunAsync had failed to release the lock, this second, independent connection
        // would be denied it.
        await using var secondConnection = new NpgsqlConnection(ConnectionString);
        await secondConnection.OpenAsync();

        var secondActionRan = false;
        var ranAgain = await BackgroundJobLock.TryRunAsync(
            secondConnection,
            key,
            "test-job",
            NullLogger.Instance,
            () =>
            {
                secondActionRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(ranAgain);
        Assert.True(secondActionRan);
    }

    private static async Task<bool> TryAcquireAsync(NpgsqlConnection connection, long key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        command.Parameters.AddWithValue("key", key);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ReleaseAsync(NpgsqlConnection connection, long key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@key)";
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteScalarAsync();
    }
}
