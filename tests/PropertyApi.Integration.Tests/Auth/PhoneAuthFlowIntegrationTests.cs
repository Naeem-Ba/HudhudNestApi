using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Integration.Tests.TestInfrastructure;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// End-to-end integration coverage for the phone OTP authentication flow.
///
/// Covered scenarios:
/// - POST /api/auth/phone/send-otp returns 200.
/// - POST /api/auth/phone/verify creates a phone-only user and returns tokens.
/// - The returned refresh token is persisted in RefreshTokens.
/// - POST /api/auth/refresh accepts the refresh token from phone login.
/// - The same OTP cannot be used twice.
/// - Verifying the same phone later returns IsNewUser=false.
/// </summary>
public sealed class PhoneAuthFlowIntegrationTests
    : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private readonly PhoneAuthWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PhoneAuthFlowIntegrationTests(PhoneAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "PhoneAuth")]
    public async Task PhoneOtp_FullFlow_CreatesUser_PersistsRefreshToken_Refreshes_RejectsUsedOtp_AndSecondLoginIsNotNew()
    {
        var phoneNumber = CreateUniqueGermanPhoneNumber();

        var sendResponse = await SendOtpAsync(phoneNumber);

        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        using (var sendJson = await ReadJsonAsync(sendResponse))
        {
            Assert.Equal(
                300,
                GetProperty(sendJson.RootElement, "ExpiresInSeconds").GetInt32());
        }

        var verifyResponse = await VerifyOtpAsync(
            phoneNumber,
            DeterministicOtpService.ValidOtp,
            firstName: "Naeem",
            lastName: "Bazzazeh");

        var verifyBody = await verifyResponse.Content.ReadAsStringAsync();

        Assert.True(
            verifyResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 OK from /api/auth/phone/verify, but got {(int)verifyResponse.StatusCode} {verifyResponse.StatusCode}. Body: {verifyBody}");

        string userId;
        string refreshToken;

        using (var verifyJson = JsonDocument.Parse(verifyBody))
        {
            var root = verifyJson.RootElement;

            Assert.True(GetProperty(root, "IsNewUser").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(GetProperty(root, "AccessToken").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(GetProperty(root, "RefreshToken").GetString()));

            refreshToken = GetProperty(root, "RefreshToken").GetString()!;

            var user = GetProperty(root, "User");
            userId = GetProperty(user, "Id").GetString()!;

            Assert.Equal(phoneNumber, GetProperty(user, "PhoneNumber").GetString());
            Assert.False(GetProperty(user, "HasEmail").GetBoolean());
            Assert.False(GetProperty(user, "HasPassword").GetBoolean());
            Assert.False(GetProperty(user, "EmailVerified").GetBoolean());
        }

        var parsedUserId = Guid.Parse(userId);

        await AssertRefreshTokenExistsAsync(
            parsedUserId,
            refreshToken,
            shouldBeRevoked: false);

        await AssertRefreshTokenCanBeLoadedWithUserAsync(
            parsedUserId,
            refreshToken);

        var refreshResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken });

        var refreshBody = await refreshResponse.Content.ReadAsStringAsync();

        Assert.True(
            refreshResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 OK from /api/auth/refresh, but got {(int)refreshResponse.StatusCode} {refreshResponse.StatusCode}. Body: {refreshBody}");

        string rotatedRefreshToken;

        using (var refreshJson = JsonDocument.Parse(refreshBody))
        {
            var root = refreshJson.RootElement;

            Assert.False(string.IsNullOrWhiteSpace(GetProperty(root, "accessToken").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(GetProperty(root, "refreshToken").GetString()));

            rotatedRefreshToken = GetProperty(root, "refreshToken").GetString()!;
        }

        Assert.NotEqual(refreshToken, rotatedRefreshToken);

        await AssertRefreshTokenExistsAsync(
            parsedUserId,
            refreshToken,
            shouldBeRevoked: true);

        await AssertRefreshTokenExistsAsync(
            parsedUserId,
            rotatedRefreshToken,
            shouldBeRevoked: false);

        var reuseResponse = await VerifyOtpAsync(
            phoneNumber,
            DeterministicOtpService.ValidOtp,
            firstName: "Naeem",
            lastName: "Bazzazeh");

        Assert.Equal(HttpStatusCode.BadRequest, reuseResponse.StatusCode);

        var secondSendResponse = await SendOtpAsync(phoneNumber);
        Assert.Equal(HttpStatusCode.OK, secondSendResponse.StatusCode);

        var secondVerifyResponse = await VerifyOtpAsync(
            phoneNumber,
            DeterministicOtpService.ValidOtp,
            firstName: "Naeem",
            lastName: "Bazzazeh");

        Assert.Equal(HttpStatusCode.OK, secondVerifyResponse.StatusCode);

        using (var secondVerifyJson = await ReadJsonAsync(secondVerifyResponse))
        {
            var root = secondVerifyJson.RootElement;

            Assert.False(GetProperty(root, "IsNewUser").GetBoolean());
            Assert.Equal(
                parsedUserId.ToString(),
                GetProperty(GetProperty(root, "User"), "Id").GetString());
        }
    }

    private Task<HttpResponseMessage> SendOtpAsync(string phoneNumber)
    {
        return _client.PostAsJsonAsync(
            "/api/auth/phone/send-otp",
            new { phoneNumber });
    }

    private Task<HttpResponseMessage> VerifyOtpAsync(
        string phoneNumber,
        string code,
        string? firstName = null,
        string? lastName = null)
    {
        return _client.PostAsJsonAsync(
            "/api/auth/phone/verify",
            new
            {
                phoneNumber,
                code,
                firstName,
                lastName
            });
    }

    private async Task AssertRefreshTokenExistsAsync(
        Guid userId,
        string refreshToken,
        bool shouldBeRevoked)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tokenHash = HashToken(refreshToken);

        var stored = await db.RefreshTokens
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(token =>
                token.UserId == userId &&
                token.TokenHash == tokenHash);

        Assert.NotNull(stored);
        Assert.Equal(shouldBeRevoked, stored!.IsRevoked);
        Assert.True(stored.ExpiresAt > DateTime.UtcNow);
    }

    private async Task AssertRefreshTokenCanBeLoadedWithUserAsync(
        Guid userId,
        string refreshToken)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tokenHash = HashToken(refreshToken);

        var stored = await db.RefreshTokens
            .IgnoreQueryFilters()
            .Include(token => token.User)
            .SingleOrDefaultAsync(token =>
                token.UserId == userId &&
                token.TokenHash == tokenHash);

        Assert.NotNull(stored);
        Assert.False(stored!.IsRevoked);
        Assert.True(stored.ExpiresAt > DateTime.UtcNow);
        Assert.NotNull(stored.User);
        Assert.False(stored.User!.IsDeleted);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.False(
            string.IsNullOrWhiteSpace(body),
            "Expected a JSON response body, but the response body was empty.");

        return JsonDocument.Parse(body);
    }

    private static JsonElement GetProperty(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new InvalidOperationException(
            $"JSON property '{propertyName}' was not found. Payload: {element}");
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string CreateUniqueGermanPhoneNumber()
    {
        var suffix = Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L);
        return $"+49{suffix:D13}";
    }
}