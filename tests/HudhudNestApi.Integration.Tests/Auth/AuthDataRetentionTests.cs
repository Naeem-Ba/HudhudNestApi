using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Infrastructure.Auth.Services;
using HudhudNestApi.Infrastructure.Persistence;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// DB security audit 2026-10-29: OTP challenges (phone numbers in clear text) and refresh tokens
/// (client IPs) were never deleted. The sweep removes only rows that can no longer be used.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Auth")]
public sealed class AuthDataRetentionTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Retention removes only OTP challenges and refresh tokens past their grace period")]
    public async Task Sweep_RemovesOnlyRowsPastTheirGrace()
    {
        var user = await _factory.SeedUserAsync($"retention-{Guid.NewGuid():N}@test.local");
        var now = DateTimeOffset.UtcNow;

        var oldOtp = Guid.NewGuid();
        var expiredToday = Guid.NewGuid();
        var oldToken = Guid.NewGuid();
        var recentlyExpiredToken = Guid.NewGuid();
        var liveToken = Guid.NewGuid();

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            const string otp =
                "INSERT INTO \"PhoneOtpChallenges\" (\"Id\", \"NormalizedPhoneNumber\", \"CodeHash\", \"Purpose\", " +
                "\"ExpiresAtUtc\", \"AttemptCount\", \"CreatedAtUtc\", \"Channel\") VALUES ({0}, '+963900000001', 'h', 1, {1}, 0, {1}, 'Sms')";
            await db.Database.ExecuteSqlRawAsync(otp, oldOtp, now.AddDays(-2));
            await db.Database.ExecuteSqlRawAsync(otp, expiredToday, now.AddHours(-2));

            const string token =
                "INSERT INTO \"RefreshTokens\" (\"Id\", \"TokenHash\", \"CreatedAt\", \"ExpiresAt\", \"IsRevoked\", \"UserId\") " +
                "VALUES ({0}, {1}, {2}, {2}, false, {3})";
            await db.Database.ExecuteSqlRawAsync(token, oldToken, Guid.NewGuid().ToString("N"), now.UtcDateTime.AddDays(-31), user.IdentityId);
            await db.Database.ExecuteSqlRawAsync(token, recentlyExpiredToken, Guid.NewGuid().ToString("N"), now.UtcDateTime.AddDays(-29), user.IdentityId);
            await db.Database.ExecuteSqlRawAsync(token, liveToken, Guid.NewGuid().ToString("N"), now.UtcDateTime.AddDays(7), user.IdentityId);
        });

        var service = new AuthDataRetentionHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<AuthDataRetentionHostedService>>());

        Assert.Equal((1, 1), await service.RunOnceAsync(CancellationToken.None, now));
        Assert.Equal((0, 0), await service.RunOnceAsync(CancellationToken.None, now));

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var otpIds = await db.PhoneOtpChallenges.Select(c => c.Id).ToListAsync();
            Assert.DoesNotContain(oldOtp, otpIds);
            Assert.Contains(expiredToday, otpIds);

            var tokenIds = await db.RefreshTokens.IgnoreQueryFilters().Select(t => t.Id).ToListAsync();
            Assert.DoesNotContain(oldToken, tokenIds);
            Assert.Contains(recentlyExpiredToken, tokenIds);
            Assert.Contains(liveToken, tokenIds);
        });
    }
}
