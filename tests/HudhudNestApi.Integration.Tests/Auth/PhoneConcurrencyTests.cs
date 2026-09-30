using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// The phone workflow relies on ExecuteUpdate reservations and unique indexes rather than locks, so the
/// races that matter are exercised for real: many requests at once against PostgreSQL. Each scenario asserts
/// the invariant (one winner, no duplicate account, no 500) instead of a particular interleaving.
/// </summary>
public sealed class PhoneConcurrencyTests : IClassFixture<PhoneLoginAuditFactory>, IAsyncLifetime
{
    private const string Password = "SecurePass9";
    private static int _seed = Environment.TickCount & 0xFFFFFF;
    private readonly PhoneLoginAuditFactory _factory;

    public PhoneConcurrencyTests(PhoneLoginAuditFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string NewPhone() => $"+9639{Interlocked.Increment(ref _seed) % 100_000_000:D8}";
    private static string NewIp() => $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}";

    private sealed record Reply(HttpStatusCode Status, JsonElement Json)
    {
        public string? Str(string name)
        {
            if (Json.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in Json.EnumerateObject())
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString();
            return null;
        }

        public string Code => Str("code") ?? Str("errorCode") ?? "";
    }

    private async Task<Reply> Post(string path, object body, string? bearer = null)
    {
        using var client = _factory.CreateClient(new() { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Test-Ip", NewIp()); // every request in its own rate-limit partition
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new Reply(response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static void AssertNoServerErrors(IEnumerable<Reply> replies) =>
        Assert.DoesNotContain(replies, r => (int)r.Status >= 500);

    private async Task<int> UserCount(string phone)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.IgnoreQueryFilters().CountAsync(u => u.NormalizedPhoneNumber == phone);
    }

    private async Task<(Guid ChallengeId, string Code)> SendRegistration(string phone)
    {
        var send = await Post("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        Assert.Equal(HttpStatusCode.OK, send.Status);
        return (Guid.Parse(send.Str("challengeId")!), _factory.Sms.LastCodeFor(phone)!);
    }

    [Fact]
    public async Task SameChallenge_VerifiedInParallel_HasExactlyOneWinner_AndOneAccount()
    {
        var phone = NewPhone();
        var (challengeId, code) = await SendRegistration(phone);

        var replies = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Post("/api/auth/phone/registration/verify",
            new { challengeId, code, password = Password, firstName = "Race", lastName = "One" })));

        AssertNoServerErrors(replies);
        Assert.Equal(1, replies.Count(r => r.Status == HttpStatusCode.OK));
        Assert.Equal(1, await UserCount(phone));
    }

    [Fact]
    public async Task SameNumber_RegisteredInParallel_ThroughDifferentChallenges_CreatesOneAccount()
    {
        var phone = NewPhone();
        // three real challenges are allowed per hour; the last code is the only one the sink remembers,
        // so read each code right after its own send
        var challenges = new List<(Guid Id, string Code)>();
        for (var i = 0; i < 3; i++) challenges.Add(await SendRegistration(phone));

        var replies = await Task.WhenAll(challenges.Select(c => Post("/api/auth/phone/registration/verify",
            new { challengeId = c.Id, code = c.Code, password = Password, firstName = "Race", lastName = "Two" })));

        AssertNoServerErrors(replies);
        Assert.Equal(1, await UserCount(phone));
        Assert.True(replies.Count(r => r.Status == HttpStatusCode.OK) <= 1);
    }

    [Fact]
    public async Task SendOtp_InParallelForOneNumber_NeverSendsMoreThanTheHourlyLimit_AndNeverFails()
    {
        var phone = NewPhone();
        var before = _factory.Sms.SendCount;

        var replies = await Task.WhenAll(Enumerable.Range(0, 15).Select(_ =>
            Post("/api/auth/phone/registration/send-otp", new { phoneNumber = phone })));

        AssertNoServerErrors(replies);
        Assert.All(replies, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        var sent = _factory.Sms.SendCount - before;
        // The count-then-insert runs under a per-number advisory lock, so a parallel burst can never
        // exceed the three-per-hour limit.
        Assert.InRange(sent, 1, 3);
    }

    [Fact]
    public async Task PasswordReset_TheSameConfirmationToken_WorksOnce()
    {
        var phone = NewPhone();
        var (regChallenge, regCode) = await SendRegistration(phone);
        var register = await Post("/api/auth/phone/registration/verify",
            new { challengeId = regChallenge, code = regCode, password = Password, firstName = "Reset", lastName = "Race" });
        Assert.Equal(HttpStatusCode.OK, register.Status);

        var send = await Post("/api/auth/phone/password-reset/send-otp", new { phoneNumber = phone });
        var challengeId = Guid.Parse(send.Str("challengeId")!);
        var verify = await Post("/api/auth/phone/password-reset/verify",
            new { challengeId, code = _factory.Sms.LastCodeFor(phone) });
        var token = verify.Str("confirmationToken")!;

        var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Post("/api/auth/phone/password-reset/confirm",
            new { phoneNumber = phone, confirmationToken = token, newPassword = $"BrandNew{i}Pass" })));

        AssertNoServerErrors(replies);
        Assert.Equal(1, replies.Count(r => r.Status == HttpStatusCode.OK));
    }

    [Fact]
    public async Task PhoneChange_ToANumberBeingRegisteredAtTheSameMoment_LeavesOneOwner_WithoutA500()
    {
        var oldPhone = NewPhone();
        var target = NewPhone();
        var (regChallenge, regCode) = await SendRegistration(oldPhone);
        var register = await Post("/api/auth/phone/registration/verify",
            new { challengeId = regChallenge, code = regCode, password = Password, firstName = "Mover", lastName = "Race" });
        var bearer = register.Str("accessToken")!;

        var changeSend = await Post("/api/auth/phone/change/send-otp", new { phoneNumber = target }, bearer);
        Assert.Equal(HttpStatusCode.OK, changeSend.Status);
        var changeChallenge = Guid.Parse(changeSend.Str("challengeId")!);
        var changeCode = _factory.Sms.LastCodeFor(target)!;

        var (otherChallenge, otherCode) = await SendRegistration(target);
        Assert.NotEqual(changeCode, otherCode); // both codes were issued for the same target number

        var replies = await Task.WhenAll(
            Post("/api/auth/phone/change/verify", new { challengeId = changeChallenge, code = changeCode, currentPassword = Password }, bearer),
            Post("/api/auth/phone/registration/verify",
                new { challengeId = otherChallenge, code = otherCode, password = Password, firstName = "Other", lastName = "Owner" }));

        AssertNoServerErrors(replies);
        Assert.Equal(1, await UserCount(target));
    }
}
