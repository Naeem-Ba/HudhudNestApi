using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Users.Commands.DeleteUser;
using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Users;

/// <summary>
/// QA Phase 5, P5-D05: a deleted account used to keep its push device tokens (install identifiers) and the
/// password hash of the anonymized identity. Runs the real handler on real Postgres.
/// </summary>
public sealed class AccountDeletionResiduesTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Deleting an account removes its device tokens and password hash, and leaves others' alone")]
    public async Task Deletion_RemovesDeviceTokensAndPasswordHash_OfThatAccountOnly()
    {
        var doomed = await _factory.SeedUserAsync("del-residue");
        var bystander = await _factory.SeedUserAsync("del-residue-other");

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            db.DeviceTokens.Add(DeviceToken.Create(doomed.Id, $"tok-a-{Guid.NewGuid():N}", DevicePlatform.Android, now));
            db.DeviceTokens.Add(DeviceToken.Create(doomed.Id, $"tok-b-{Guid.NewGuid():N}", DevicePlatform.Ios, now));
            db.DeviceTokens.Add(DeviceToken.Create(bystander.Id, $"tok-c-{Guid.NewGuid():N}", DevicePlatform.Android, now));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var before = await _factory.InScopeAsync(async services =>
            await services.GetRequiredService<AppDbContext>().Users.AsNoTracking()
                .SingleAsync(u => u.Id == doomed.Id));
        Assert.False(string.IsNullOrEmpty(before.PasswordHash));

        var result = await _factory.InScopeAsync(services =>
            services.GetRequiredService<DeleteUserCommandHandler>()
                .ExecuteScheduledDeletionAsync(doomed.Id, CancellationToken.None));
        Assert.True(result.Success, string.Join(",", result.Errors));

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await db.DeviceTokens.CountAsync(t => t.UserId == doomed.Id));
            Assert.Equal(1, await db.DeviceTokens.CountAsync(t => t.UserId == bystander.Id));

            var after = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == doomed.Id);
            Assert.True(after.IsDeleted);
            Assert.Null(after.PasswordHash);

            var other = await db.Users.AsNoTracking().SingleAsync(u => u.Id == bystander.Id);
            Assert.False(string.IsNullOrEmpty(other.PasswordHash));
        });
    }
}
