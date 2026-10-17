using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Notifications.Push;

namespace HudhudNestApi.Infrastructure.Notifications.Push;

/// <summary>
/// Firebase Cloud Messaging HTTP v1. One sender serves Android directly and iOS through APNs
/// (FCM relays it; the APNs key lives in the Firebase project, not here).
///
/// Authentication is the standard service-account OAuth2 flow (RS256-signed JWT exchanged for a
/// short-lived access token), implemented with the platform crypto so the solution takes no new
/// dependency. The token is cached until shortly before it expires.
/// </summary>
public sealed class FcmPushSender : IPushSender
{
    public const string HttpClientName = "fcm";

    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly IHttpClientFactory _httpClients;
    private readonly PushOptions _options;
    private readonly ILogger<FcmPushSender> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTime _accessTokenExpiresUtc = DateTime.MinValue;
    private ServiceAccount? _account;

    public FcmPushSender(
        IHttpClientFactory httpClients,
        IOptions<PushOptions> options,
        ILogger<FcmPushSender> logger)
    {
        _httpClients = httpClients;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PushSendStatus> SendAsync(string deviceToken, PushPayload payload, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            return PushSendStatus.Disabled;
        }

        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "FCM authentication failed; push is unavailable until credentials are fixed.");
            return PushSendStatus.Failed;
        }

        var body = BuildMessage(deviceToken, payload);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(_options.ProjectId!)}/messages:send")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var client = _httpClients.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return PushSendStatus.Sent;
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.NotFound || error.Contains("UNREGISTERED", StringComparison.Ordinal))
            {
                return PushSendStatus.InvalidToken;
            }

            // Status only: the response echoes the token and could echo the message.
            _logger.LogWarning("FCM rejected a push. Status={Status}", (int)response.StatusCode);
            return PushSendStatus.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "FCM request failed.");
            return PushSendStatus.Failed;
        }
    }

    public static string BuildMessage(string deviceToken, PushPayload payload)
    {
        var data = new Dictionary<string, string>
        {
            ["type"] = payload.Type.ToString(),
            ["notificationId"] = payload.NotificationId.ToString(),
        };
        if (payload.PropertyId is { } propertyId)
        {
            data["propertyId"] = propertyId.ToString();
        }

        if (payload.RelatedEntityId is { } relatedId)
        {
            data["relatedEntityId"] = relatedId.ToString();
        }

        // Which screen a tap opens. Ids only; the app re-checks access when it opens the screen.
        if (!string.IsNullOrWhiteSpace(payload.TargetKind))
        {
            data["targetKind"] = payload.TargetKind;
        }

        if (!string.IsNullOrWhiteSpace(payload.Category))
        {
            data["category"] = payload.Category;
        }

        var message = new
        {
            message = new
            {
                token = deviceToken,
                notification = new { title = payload.Title, body = payload.Body },
                data,
                android = new { priority = "HIGH", notification = new { channel_id = "hudhudnest_default" } },
                apns = new { payload = new { aps = new { sound = "default" } } },
            },
        };

        return JsonSerializer.Serialize(message);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpiresUtc)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpiresUtc)
            {
                return _accessToken;
            }

            _account ??= ServiceAccount.Parse(_options.ServiceAccountJson!);

            var assertion = CreateJwt(_account);
            using var request = new HttpRequestMessage(HttpMethod.Post, _account.TokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion,
                }),
            };

            var client = _httpClients.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            _accessToken = doc.RootElement.GetProperty("access_token").GetString()
                           ?? throw new InvalidOperationException("No access token in the response.");
            var lifetime = doc.RootElement.TryGetProperty("expires_in", out var expires)
                ? expires.GetInt32()
                : 3600;
            _accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, lifetime - 120));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string CreateJwt(ServiceAccount account)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = account.ClientEmail,
            scope = Scope,
            aud = account.TokenUri,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddMinutes(55).ToUnixTimeSeconds(),
        }));

        var unsigned = $"{header}.{claims}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(account.PrivateKey);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsigned),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{unsigned}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record ServiceAccount(string ClientEmail, string PrivateKey, string TokenUri)
    {
        public static ServiceAccount Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new ServiceAccount(
                root.GetProperty("client_email").GetString()!,
                root.GetProperty("private_key").GetString()!,
                root.TryGetProperty("token_uri", out var uri)
                    ? uri.GetString()!
                    : "https://oauth2.googleapis.com/token");
        }
    }
}
