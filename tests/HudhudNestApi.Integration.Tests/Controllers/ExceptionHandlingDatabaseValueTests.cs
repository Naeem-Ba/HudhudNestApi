using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using HudhudNestApi.Middleware;

namespace HudhudNestApi.Integration.Tests.Controllers;

/// <summary>QA Phase 8, P8-D01: values PostgreSQL cannot store are the caller's input, never a 500.</summary>
public sealed class ExceptionHandlingDatabaseValueTests
{
    private static async Task<(int Status, string Body)> RunAsync(Exception thrown)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw thrown, NullLogger<ExceptionHandlingMiddleware>.Instance, env.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private static DbUpdateException Db(string sqlState)
        => new("save failed", new PostgresException("message", "ERROR", "ERROR", sqlState));

    [Theory]
    [InlineData("22021")] // character not in repertoire: a NUL in text
    [InlineData("22003")] // numeric value out of range
    public async Task ValuesTheDatabaseCannotStore_Are422(string sqlState)
    {
        var (status, body) = await RunAsync(Db(sqlState));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status);
        Assert.DoesNotContain("save failed", body);
    }

    [Theory]
    [InlineData("23503")] // a foreign-key violation can be a server bug: it must stay loud
    [InlineData("40001")]
    public async Task OtherDatabaseErrors_StayA500(string sqlState)
    {
        var (status, _) = await RunAsync(Db(sqlState));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
    }
}
