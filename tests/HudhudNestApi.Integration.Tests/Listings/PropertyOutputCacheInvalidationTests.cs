using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using HudhudNestApi.Application.Common.Caching;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// PropertiesController.GetAll/GetById carry [OutputCache] policies tagged "properties" (see
/// OutputCacheRegistration) -- policies that existed, unused, for a long time before anything
/// actually applied them. Turning the cache on without also invalidating it on every write would
/// have been a worse bug than no caching at all: an edited price, a newly published listing, or
/// a deleted/unpublished one could all be served stale from the public endpoints for the rest of
/// the policy's TTL (30-60s). This suite proves AppDbContext.SaveChangesAsync evicts the
/// "properties" output-cache tag on exactly the writes that change what those endpoints return,
/// and does not evict it (or throw) otherwise.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class PropertyOutputCacheInvalidationTests
{
    private static AppDbContext CreateContext(string databaseName, IOutputCacheStore? outputCacheStore)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options, new EphemeralDataProtectionProvider(), outputCacheStore);
    }

    private static Property NewProperty(bool isPublished = true) =>
        Property.Create(
            title: "شقة للبيع في طرطوس",
            description: "وصف تجريبي",
            ownerId: Guid.NewGuid(),
            listingType: ListingType.ForSale,
            isPublished: isPublished);

    [Fact]
    public async Task SaveChanges_AddingAProperty_EvictsThePropertiesTag()
    {
        var store = new Mock<IOutputCacheStore>();
        await using var db = CreateContext(Guid.NewGuid().ToString(), store.Object);

        db.Properties.Add(NewProperty());
        await db.SaveChangesAsync();

        store.Verify(
            x => x.EvictByTagAsync(OutputCacheTags.Properties, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveChanges_UpdatingAProperty_EvictsThePropertiesTag()
    {
        var store = new Mock<IOutputCacheStore>();
        var databaseName = Guid.NewGuid().ToString();

        var property = NewProperty();
        await using (var seed = CreateContext(databaseName, outputCacheStore: null))
        {
            seed.Properties.Add(property);
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, store.Object);
        var tracked = await db.Properties.SingleAsync(p => p.Id == property.Id);
        tracked.UpdateTitle("عنوان جديد");
        await db.SaveChangesAsync();

        store.Verify(
            x => x.EvictByTagAsync(OutputCacheTags.Properties, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveChanges_SoftDeletingAProperty_StillEvictsThePropertiesTag()
    {
        // Regression guard: the soft-delete interception earlier in SaveChangesAsync flips
        // EntityState.Deleted to Modified before this check runs -- confirms that flip doesn't
        // accidentally hide a delete from the cache-invalidation check (which only looks at
        // Added/Modified, not Deleted).
        var store = new Mock<IOutputCacheStore>();
        var databaseName = Guid.NewGuid().ToString();

        var property = NewProperty();
        await using (var seed = CreateContext(databaseName, outputCacheStore: null))
        {
            seed.Properties.Add(property);
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, store.Object);
        var tracked = await db.Properties.SingleAsync(p => p.Id == property.Id);
        db.Properties.Remove(tracked);
        await db.SaveChangesAsync();

        store.Verify(
            x => x.EvictByTagAsync(OutputCacheTags.Properties, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveChanges_AddingAPropertyImage_EvictsThePropertiesTag()
    {
        // PropertyDto.ImageUrls/MainImageUrl come from the PropertyImage table, not a column on
        // Property itself -- an image upload/delete/set-main never touches Property's own row,
        // so it would otherwise go unnoticed by a check that only looked at Property.
        var store = new Mock<IOutputCacheStore>();
        var databaseName = Guid.NewGuid().ToString();

        var property = NewProperty();
        await using (var seed = CreateContext(databaseName, outputCacheStore: null))
        {
            seed.Properties.Add(property);
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, store.Object);
        db.PropertyImages.Add(new PropertyImage { PropertyId = property.Id, Url = "https://cdn.example.com/a.jpg" });
        await db.SaveChangesAsync();

        store.Verify(
            x => x.EvictByTagAsync(OutputCacheTags.Properties, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveChanges_WithNoPropertyRelatedChanges_NeverEvictsTheTag()
    {
        var store = new Mock<IOutputCacheStore>();
        await using var db = CreateContext(Guid.NewGuid().ToString(), store.Object);

        // A property-unrelated write (favorites etc. would need extra FK setup to insert here;
        // an empty SaveChangesAsync call -- nothing tracked -- is the simplest honest case of
        // "no property-affecting change happened").
        await db.SaveChangesAsync();

        store.Verify(
            x => x.EvictByTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveChanges_WithNoOutputCacheStoreConfigured_DoesNotThrow()
    {
        // The parameterless-for-OutputCache constructor AppDbContext(options) -- used throughout
        // the test suite and by design-time tooling -- passes null here. A property write must
        // still save successfully with no cache store to evict against.
        await using var db = CreateContext(Guid.NewGuid().ToString(), outputCacheStore: null);

        db.Properties.Add(NewProperty());
        var exception = await Record.ExceptionAsync(() => db.SaveChangesAsync());

        Assert.Null(exception);
    }
}
