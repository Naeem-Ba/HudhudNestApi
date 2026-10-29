using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Domain.Marketing.Entities;
using HudhudNestApi.Domain.Messaging.Entities;
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

        Assert.Equal((1, 1, 0), await service.RunOnceAsync(CancellationToken.None, now));
        Assert.Equal((0, 0, 0), await service.RunOnceAsync(CancellationToken.None, now));

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

    [Fact(DisplayName = "Retention clears IP addresses of old contact messages and leads, keeps the rows")]
    public async Task Sweep_ClearsOldIpAddresses_AndKeepsTheRows()
    {
        var now = DateTimeOffset.UtcNow;
        var oldMessage = new ContactMessage { Name = "a", Email = "a@test.local", Body = "b", IpAddress = "10.0.0.1", CreatedAt = now.UtcDateTime.AddDays(-91) };
        var newMessage = new ContactMessage { Name = "a", Email = "a@test.local", Body = "b", IpAddress = "10.0.0.2", CreatedAt = now.UtcDateTime.AddDays(-89) };
        var oldLead = Lead.Create("Lead", "+963900000002", "Damascus", "buyer", "landing", ipAddress: "10.0.0.3");
        oldLead.CreatedAt = now.UtcDateTime.AddDays(-91);
        var newLead = Lead.Create("Lead", "+963900000003", "Damascus", "buyer", "landing", ipAddress: "10.0.0.4");

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            db.AddRange(oldMessage, newMessage, oldLead, newLead);
            await db.SaveChangesAsync();
            // SaveChanges stamps CreatedAt only when empty; force the old dates.
            await db.ContactMessages.Where(m => m.Id == oldMessage.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.CreatedAt, now.UtcDateTime.AddDays(-91)));
            await db.Leads.Where(l => l.Id == oldLead.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(l => l.CreatedAt, now.UtcDateTime.AddDays(-91)));
        });

        var off = new AuthDataRetentionHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<AuthDataRetentionHostedService>>(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [AuthDataRetentionHostedService.IpAddressDaysKey] = "0" })
                .Build());
        Assert.Equal(0, (await off.RunOnceAsync(CancellationToken.None, now)).IpAddresses);

        var service = new AuthDataRetentionHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<AuthDataRetentionHostedService>>());
        Assert.Equal(2, (await service.RunOnceAsync(CancellationToken.None, now)).IpAddresses);
        Assert.Equal(0, (await service.RunOnceAsync(CancellationToken.None, now)).IpAddresses);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var messages = await db.ContactMessages.AsNoTracking()
                .Where(m => m.Id == oldMessage.Id || m.Id == newMessage.Id)
                .ToDictionaryAsync(m => m.Id, m => m.IpAddress);
            Assert.Null(messages[oldMessage.Id]);
            Assert.Equal("10.0.0.2", messages[newMessage.Id]);

            var leads = await db.Leads.AsNoTracking()
                .Where(l => l.Id == oldLead.Id || l.Id == newLead.Id)
                .ToDictionaryAsync(l => l.Id, l => l.IpAddress);
            Assert.Null(leads[oldLead.Id]);
            Assert.Equal("10.0.0.4", leads[newLead.Id]);
        });
    }
}
