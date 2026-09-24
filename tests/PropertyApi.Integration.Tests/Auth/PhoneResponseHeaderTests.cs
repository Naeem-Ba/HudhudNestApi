using System.Net.Http.Json;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// Responses of the authentication endpoints carry access tokens, one-time challenge ids and account
/// status. They must never be stored by a browser or an intermediary cache, whatever the status code.
/// </summary>
public sealed class PhoneResponseHeaderTests : IClassFixture<PhoneLoginAuditFactory>, IAsyncLifetime
{
    private readonly PhoneLoginAuditFactory _factory;

    public PhoneResponseHeaderTests(PhoneLoginAuditFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string NewIp() => $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}";

    private async Task<HttpResponseMessage> Post(string path, object body)
    {
        using var client = _factory.CreateClient(new() { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Test-Ip", NewIp());
        return await client.SendAsync(request);
    }

    [Theory]
    [InlineData("/api/auth/phone/login")]
    [InlineData("/api/auth/phone/registration/send-otp")]
    [InlineData("/api/auth/phone/password-reset/send-otp")]
    [InlineData("/api/auth/refresh")]
    public async Task AuthResponses_AreMarkedNoStore(string path)
    {
        var response = await Post(path, new { phoneNumber = "+963944000111", password = "WrongPass1" });

        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.NoStore, $"{path} response is cacheable");
    }
}
