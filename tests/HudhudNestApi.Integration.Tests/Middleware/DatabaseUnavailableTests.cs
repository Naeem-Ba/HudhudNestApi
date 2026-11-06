using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using HudhudNestApi.Middleware;

namespace HudhudNestApi.Integration.Tests.Middleware;

/// <summary>
/// D14: when the database cannot be reached the API used to answer 500. It is a temporary condition of the service,
/// so it answers 503 with Retry-After. Bugs, bad input and constraint violations keep their own answers.
/// </summary>
public sealed class DatabaseUnavailableTests
{
    private static async Task<(int Status, string? RetryAfter, JsonElement Body)> RunAsync(Exception toThrow, bool aborted = false)
    {
        using var cts = new CancellationTokenSource();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestAborted = cts.Token;
        if (aborted)
        {
            await cts.CancelAsync();
        }

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw toThrow,
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            new TestEnvironment("Production"));

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var body = text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        var retry = context.Response.Headers.RetryAfter.ToString();
        return (context.Response.StatusCode, string.IsNullOrEmpty(retry) ? null : retry, body);
    }

    public static IEnumerable<object[]> Outages()
    {
        yield return new object[] { new NpgsqlException("Failed to connect to 127.0.0.1:5432", new SocketException(111)) };
        yield return new object[] { new NpgsqlException("Exception while reading from stream", new IOException("broken pipe")) };
        yield return new object[] { new NpgsqlException("Timeout during connection", new TimeoutException()) };
        yield return new object[] { new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", new NpgsqlException("x", new SocketException(110))) };
        yield return new object[] { new PostgresException("terminating connection due to administrator command", "FATAL", "FATAL", PostgresErrorCodes.AdminShutdown) };
        yield return new object[] { new PostgresException("sorry, too many clients already", "FATAL", "FATAL", PostgresErrorCodes.TooManyConnections) };
        yield return new object[] { new PostgresException("the database system is starting up", "FATAL", "FATAL", PostgresErrorCodes.CannotConnectNow) };
        yield return new object[] { new DbUpdateException("save failed", new NpgsqlException("x", new IOException())) };
    }

    [Theory(DisplayName = "A database outage answers 503 with Retry-After and no internal detail")]
    [MemberData(nameof(Outages))]
    [Trait("Category", "Integration")]
    public async Task Outage_Is503(Exception failure)
    {
        var (status, retryAfter, body) = await RunAsync(failure);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("5", retryAfter);
        Assert.Equal("SERVICE_UNAVAILABLE", body.GetProperty("code").GetString());
        Assert.DoesNotContain("127.0.0.1", body.ToString(), StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> NotOutages()
    {
        yield return new object[] { new InvalidOperationException("a bug") };
        yield return new object[] { new PostgresException("syntax error", "ERROR", "ERROR", PostgresErrorCodes.SyntaxError) };
        yield return new object[] { new PostgresException("deadlock", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected) };
        yield return new object[] { new NpgsqlException("protocol violation") };
    }

    [Theory(DisplayName = "Bugs and query errors stay 500")]
    [MemberData(nameof(NotOutages))]
    [Trait("Category", "Integration")]
    public async Task OtherFailures_Stay500(Exception failure)
    {
        var (status, retryAfter, _) = await RunAsync(failure);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Null(retryAfter);
    }

    [Fact(DisplayName = "A real failed connection (nothing listens on the port) answers 503")]
    [Trait("Category", "Integration")]
    public async Task RealRefusedConnection_Is503()
    {
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 1,
            Database = "none",
            Username = "none",
            Password = "none",
            Timeout = 2
        }.ConnectionString;

        Exception captured;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            throw new InvalidOperationException("The connection unexpectedly opened.");
        }
        catch (NpgsqlException ex)
        {
            captured = ex;
        }

        var (status, retryAfter, _) = await RunAsync(captured);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("5", retryAfter);
    }

    [Fact(DisplayName = "A client that already went away is not answered with 503")]
    [Trait("Category", "Integration")]
    public async Task AbortedRequest_IsNotTurnedInto503()
    {
        var (status, _, _) = await RunAsync(new NpgsqlException("x", new IOException()), aborted: true);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
