using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Security;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>Launch audit A4-001: the auth proxy's client IP is trusted only with the shared secret.</summary>
public sealed class EdgeClientIpMiddlewareTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef-launch";
    private static readonly IPAddress ProxyIp = IPAddress.Parse("198.51.100.10");

    private static async Task<HttpContext> RunAsync(string? configuredSecret, string? clientIp, string? presentedSecret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["EdgeProxy:SharedSecret"] = configuredSecret })
            .Build();

        HttpContext? seen = null;
        var middleware = new EdgeClientIpMiddleware(
            ctx => { seen = ctx; return Task.CompletedTask; },
            config,
            NullLogger<EdgeClientIpMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = ProxyIp;
        if (clientIp is not null) context.Request.Headers[EdgeClientIpMiddleware.ClientIpHeader] = clientIp;
        if (presentedSecret is not null) context.Request.Headers[EdgeClientIpMiddleware.EdgeSecretHeader] = presentedSecret;

        await middleware.InvokeAsync(context);
        return seen!;
    }

    [Fact]
    public async Task Matching_secret_sets_the_visitor_ip()
    {
        var ctx = await RunAsync(Secret, "203.0.113.7", Secret);

        Assert.Equal(IPAddress.Parse("203.0.113.7"), ctx.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData(Secret, "203.0.113.7", "wrong-secret-wrong-secret-wrong-secret")]
    [InlineData(Secret, "203.0.113.7", null)]
    [InlineData(null, "203.0.113.7", Secret)]
    [InlineData("too-short", "203.0.113.7", "too-short")]
    [InlineData(Secret, "not-an-ip", Secret)]
    public async Task Anything_else_keeps_the_connection_ip(string? configured, string? ip, string? presented)
    {
        var ctx = await RunAsync(configured, ip, presented);

        Assert.Equal(ProxyIp, ctx.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Both_headers_are_removed_before_the_rest_of_the_pipeline()
    {
        var ctx = await RunAsync(Secret, "203.0.113.7", Secret);

        Assert.False(ctx.Request.Headers.ContainsKey(EdgeClientIpMiddleware.ClientIpHeader));
        Assert.False(ctx.Request.Headers.ContainsKey(EdgeClientIpMiddleware.EdgeSecretHeader));
    }
}
