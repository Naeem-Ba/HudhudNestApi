using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace PropertyApi.Infrastructure.Persistence;

/// <summary>
/// Session-level Postgres advisory lock for recurring background jobs. Not transactional
/// (unlike the one existing advisory-lock usage in DatabaseSeeder.cs, which wraps a single
/// transaction): a sweep here runs several sequential queries and SaveChangesAsync calls, so
/// the lock must be held for the connection's whole lifetime, acquired before the work starts
/// and released after it ends — not tied to any single transaction.
/// </summary>
internal static class BackgroundJobLock
{
    /// <summary>
    /// Attempts to acquire <paramref name="key"/> on <paramref name="connection"/> (which
    /// must already be open) and, only if acquired, runs <paramref name="action"/>. Returns
    /// whether the action ran. If another instance already holds the key, this returns
    /// immediately without running the action — that instance's own sweep already covers
    /// this cycle's work.
    /// </summary>
    public static async Task<bool> TryRunAsync(
        DbConnection connection,
        long key,
        string jobName,
        ILogger logger,
        Func<Task> action,
        CancellationToken ct)
    {
        if (!await TryAcquireAsync(connection, key, ct))
        {
            logger.LogDebug(
                "{JobName} skipped this cycle: another instance holds advisory lock {Key}.",
                jobName,
                key);
            return false;
        }

        try
        {
            await action();
            return true;
        }
        finally
        {
            // Always released, even if the action was cancelled mid-flight. A session-level
            // lock that is never unlocked stays held on the pooled connection until that
            // physical connection is destroyed, not merely closed — the same reasoning that
            // keeps unrelated cleanup off the caller's CancellationToken elsewhere in this
            // codebase (see the email-send path).
            await ReleaseAsync(connection, key);
        }
    }

    private static async Task<bool> TryAcquireAsync(DbConnection connection, long key, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        AddKeyParameter(command, key);

        var result = await command.ExecuteScalarAsync(ct);
        return result is bool acquired && acquired;
    }

    private static async Task ReleaseAsync(DbConnection connection, long key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@key)";
        AddKeyParameter(command, key);

        // CancellationToken.None deliberately: release must run even when the caller's
        // token is already cancelled.
        await command.ExecuteScalarAsync(CancellationToken.None);
    }

    private static void AddKeyParameter(DbCommand command, long key)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
    }
}
