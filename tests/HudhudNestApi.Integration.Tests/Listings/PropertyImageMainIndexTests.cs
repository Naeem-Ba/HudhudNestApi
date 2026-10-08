using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// STG-001/STG-002 against a real PostgreSQL server: PropertyImage is soft-deleted and
/// IX_PropertyImages_OneMainPerProperty (unique, WHERE IsMain) ignores IsDeleted, so a soft-deleted
/// main image must not keep IsMain=true or the next main image on that property is rejected.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class PropertyImageMainIndexTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"imgmain-{Guid.NewGuid():N}";

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Removing the main image clears IsMain so the next image can become main (delete, then upload)")]
    public async Task RemovedMainImage_DoesNotBlockNextMain()
    {
        var owner = await _factory.SeedUserAsync($"{_marker}@test.local");
        Guid propertyId, firstId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var property = Property.Create($"{_marker} p", "وصف كافٍ للإعلان", owner.UserAccountId, ListingType.ForRent);
            var first = new PropertyImage { Url = "u1", PublicId = "p1", IsMain = true, SortOrder = 0 };
            property.Images.Add(first);
            db.Properties.Add(property);
            await db.SaveChangesAsync();
            propertyId = property.Id;
            firstId = first.Id;
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var image = await db.PropertyImages.SingleAsync(i => i.Id == firstId);
            db.PropertyImages.Remove(image); // soft delete through the SaveChanges interceptor
            await db.SaveChangesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deleted = await db.PropertyImages.IgnoreQueryFilters().SingleAsync(i => i.Id == firstId);
            Assert.True(deleted.IsDeleted);
            Assert.False(deleted.IsMain);

            // The upload handler assigns IsMain=true when no live image exists; this used to violate the index.
            db.PropertyImages.Add(new PropertyImage
            {
                PropertyId = propertyId, Url = "u2", PublicId = "p2", IsMain = true, SortOrder = 0
            });
            await db.SaveChangesAsync();
        }
    }
}
