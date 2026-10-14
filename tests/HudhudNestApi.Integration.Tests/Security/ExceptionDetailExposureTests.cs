using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Middleware;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// QA phase 6 note: stack traces and exception types may only appear in 500 bodies in the
/// development-like environments. Pins the rule so a later edit to the environment list cannot
/// silently expose them on Staging or Production.
/// </summary>
public sealed class ExceptionDetailExposureTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task UnhandledException_InDeployedEnvironments_HidesDetail(string environment)
    {
        var body = await InvokeThrowingAsync(environment);

        Assert.Equal(500, body.status);
        Assert.False(body.root.TryGetProperty("detail", out _));
        Assert.False(body.root.TryGetProperty("exception", out _));
        Assert.DoesNotContain("secret-internal-text", body.raw);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("CI")]
    public async Task UnhandledException_InDevelopmentLikeEnvironments_ExposesDetail(string environment)
    {
        var body = await InvokeThrowingAsync(environment);

        Assert.Equal(500, body.status);
        Assert.True(body.root.TryGetProperty("detail", out _));
    }

    private static async Task<(int status, JsonElement root, string raw)> InvokeThrowingAsync(string environment)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(x => x.EnvironmentName).Returns(environment);

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("secret-internal-text"),
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            env.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var raw = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var doc = JsonDocument.Parse(raw);
        return (context.Response.StatusCode, doc.RootElement.Clone(), raw);
    }
}
