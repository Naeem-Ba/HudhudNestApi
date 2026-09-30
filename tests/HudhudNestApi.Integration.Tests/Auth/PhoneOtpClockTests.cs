using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// The OTP lifetime (5 minutes) and the "3 real challenges per hour" window are enforced against the
/// application clock. These tests move a manual clock across the boundaries instead of sleeping.
/// </summary>
public sealed class PhoneOtpClockTests : IClassFixture<PhoneOtpClockTests.ClockFactoryHolder>, IAsyncLifetime
{
    private const string Password = "SecurePass9";
    private static int _seed = Environment.TickCount & 0xFFFFFF;

    /// <summary>xUnit needs a fixture type; this one owns the factory and its clock.</summary>
    public sealed class ClockFactoryHolder : IDisposable
    {
        public ManualTimeProvider Clock { get; } = new();
        public PhoneLoginAuditFactory Factory { get; }
        public ClockFactoryHolder() => Factory = new PhoneLoginAuditFactory { Clock = Clock };
        public void Dispose() => Factory.Dispose();
    }

    private readonly ClockFactoryHolder _holder;
    public PhoneOtpClockTests(ClockFactoryHolder holder) => _holder = holder;

    public Task InitializeAsync() => _holder.Factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string NewPhone() => $"+9639{Interlocked.Increment(ref _seed) % 100_000_000:D8}";
    private static string NewIp() => $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}";

    private async Task<(HttpStatusCode Status, JsonElement Json)> Post(string path, object body)
    {
        using var client = _holder.Factory.CreateClient(new() { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Test-Ip", NewIp());
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string? Str(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && TryGet(json, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static JsonElement? TryGet(JsonElement json, string name)
    {
        foreach (var p in json.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private async Task<(Guid ChallengeId, string Code)> Send(string phone)
    {
        var send = await Post("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        Assert.Equal(HttpStatusCode.OK, send.Status);
        return (Guid.Parse(Str(send.Json, "challengeId")!), _holder.Factory.Sms.LastCodeFor(phone)!);
    }

    private Task<(HttpStatusCode Status, JsonElement Json)> Verify(Guid challengeId, string code) =>
        Post("/api/auth/phone/registration/verify",
            new { challengeId, code, password = Password, firstName = "Clock", lastName = "Test" });

    [Fact]
    public async Task Code_IsAccepted_JustBeforeFiveMinutes()
    {
        var (challengeId, code) = await Send(NewPhone());
        _holder.Clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(50));

        var verify = await Verify(challengeId, code);

        Assert.Equal(HttpStatusCode.OK, verify.Status);
    }

    [Fact]
    public async Task Code_IsRejected_AsExpired_JustAfterFiveMinutes()
    {
        var (challengeId, code) = await Send(NewPhone());
        _holder.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(10));

        var verify = await Verify(challengeId, code);

        Assert.Equal(HttpStatusCode.BadRequest, verify.Status);
        Assert.Contains("EXPIRED", (Str(verify.Json, "errorCode") ?? Str(verify.Json, "code") ?? "").ToUpperInvariant());
    }

    [Fact]
    public async Task HourlyChallengeLimit_Reopens_AfterTheWindow()
    {
        var phone = NewPhone();
        var sms = _holder.Factory.Sms;
        for (var i = 0; i < 3; i++) await Send(phone);
        var afterThree = sms.SendCount;

        await Send(phone); // fourth inside the hour: answered without sending
        Assert.Equal(afterThree, sms.SendCount);

        _holder.Clock.Advance(TimeSpan.FromMinutes(61));
        await Send(phone);

        Assert.Equal(afterThree + 1, sms.SendCount);
    }
}
