using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Identity.Services;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// D11: a lost <c>ConcurrencyStamp</c> race on the user row (two sign-ins of one account at once) used to leave the
/// stale change tracked, so the next SaveChanges in the same request (the audit entry) threw and the caller got 409.
/// </summary>
public sealed class BestEffortUserUpdateTests
{
    private static ServiceProvider Build(string database)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerProvider.Instance));
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(database));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<AppDbContext>();
        return services.BuildServiceProvider();
    }

    [Fact(DisplayName = "A lost concurrency race on the user row is dropped, so a later SaveChanges in the request still succeeds")]
    [Trait("Category", "Integration")]
    public async Task LostRace_DoesNotPoisonTheNextSave()
    {
        await using var provider = Build($"best-effort-{Guid.NewGuid():N}");
        Guid id;
        await using (var seed = provider.CreateAsyncScope())
        {
            var manager = seed.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "racer", Email = "racer@example.test" };
            Assert.True((await manager.CreateAsync(user)).Succeeded);
            id = user.Id;
        }

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var firstUsers = first.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var secondUsers = second.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var firstUser = (await firstUsers.FindByIdAsync(id.ToString()))!;
        var secondUser = (await secondUsers.FindByIdAsync(id.ToString()))!;

        firstUser.LastLoginAt = DateTime.UtcNow;
        Assert.True((await BestEffortUserUpdate.UpdateAsync(
            firstUsers, first.ServiceProvider.GetRequiredService<AppDbContext>(), firstUser)).Succeeded);

        var secondDb = second.ServiceProvider.GetRequiredService<AppDbContext>();
        secondUser.LastLoginAt = DateTime.UtcNow;
        var lost = await BestEffortUserUpdate.UpdateAsync(secondUsers, secondDb, secondUser);

        Assert.False(lost.Succeeded);
        Assert.Contains(lost.Errors, e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        Assert.Equal(EntityState.Detached, secondDb.Entry(secondUser).State);
        await secondDb.SaveChangesAsync();
    }

    [Fact(DisplayName = "Without discarding, the lost race would have failed the next SaveChanges (guard for the regression)")]
    [Trait("Category", "Integration")]
    public async Task WithoutDiscarding_TheNextSaveThrows()
    {
        await using var provider = Build($"best-effort-ctl-{Guid.NewGuid():N}");
        Guid id;
        await using (var seed = provider.CreateAsyncScope())
        {
            var manager = seed.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "racer2", Email = "racer2@example.test" };
            Assert.True((await manager.CreateAsync(user)).Succeeded);
            id = user.Id;
        }

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var firstUsers = first.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var secondUsers = second.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var firstUser = (await firstUsers.FindByIdAsync(id.ToString()))!;
        var secondUser = (await secondUsers.FindByIdAsync(id.ToString()))!;

        firstUser.LastLoginAt = DateTime.UtcNow;
        await firstUsers.UpdateAsync(firstUser);
        secondUser.LastLoginAt = DateTime.UtcNow;
        Assert.False((await secondUsers.UpdateAsync(secondUser)).Succeeded);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => second.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync());
    }
}
