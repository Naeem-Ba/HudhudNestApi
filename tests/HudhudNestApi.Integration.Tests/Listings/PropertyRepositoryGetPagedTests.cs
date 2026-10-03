using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Repositories;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// PropertyRepository.GetPagedAsync against a real PostgreSQL server. The page window is chosen
/// on ids only and the entities (owner, main image) are loaded in a second query, because
/// sorting whole ~4.7 KB rows by the computed featured-first key was the dominant cost of the
/// public list (see docs/performance, PERFORMANCE-AUDIT-2026-10-03 B1). Both queries are EF
/// translations, so only a real server proves the result is the same as the single query was:
/// same order, same page boundaries, same total, owner and main image still attached.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class PropertyRepositoryGetPagedTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"paged-{Guid.NewGuid():N}";

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "GetPagedAsync keeps featured-first then newest-first order across page boundaries and returns the full total")]
    public async Task DefaultOrder_FeaturedFirstThenNewest_AcrossPages()
    {
        await SeedAsync();

        var page1 = await GetAsync(new PropertyFilterDto { Page = 1, PageSize = 2, SearchTerm = _marker });
        var page2 = await GetAsync(new PropertyFilterDto { Page = 2, PageSize = 2, SearchTerm = _marker });
        var page3 = await GetAsync(new PropertyFilterDto { Page = 3, PageSize = 2, SearchTerm = _marker });
        var page4 = await GetAsync(new PropertyFilterDto { Page = 4, PageSize = 2, SearchTerm = _marker });

        // p4 is the oldest but featured, so it leads; the rest are newest first.
        Assert.Equal(["p4", "p0"], Titles(page1));
        Assert.Equal(["p1", "p2"], Titles(page2));
        Assert.Equal(["p3", "p5"], Titles(page3));
        Assert.Empty(page4.Items);
        Assert.All(new[] { page1, page2, page3, page4 }, p => Assert.Equal(6, p.TotalCount));
    }

    [Fact(DisplayName = "GetPagedAsync still attaches the owner and only the main image to each listed property")]
    public async Task Page_IncludesOwnerAndMainImageOnly()
    {
        await SeedAsync();

        var result = await GetAsync(new PropertyFilterDto { Page = 1, PageSize = 10, SearchTerm = _marker });

        Assert.All(result.Items, p => Assert.NotNull(p.Owner));
        var withImage = result.Items.Single(p => p.Title.EndsWith("p0", StringComparison.Ordinal));
        var image = Assert.Single(withImage.Images);
        Assert.True(image.IsMain);
        Assert.Equal("https://cdn.example.test/p0-main.jpg", image.Url);
        Assert.All(
            result.Items.Where(p => p.Id != withImage.Id),
            p => Assert.Empty(p.Images));
    }

    [Fact(DisplayName = "GetPagedAsync honours an explicit sort and keeps pages stable when sort keys tie")]
    public async Task ExplicitSort_ColdRentAscending_WithTies()
    {
        await SeedAsync();

        var all = await GetAsync(new PropertyFilterDto
        {
            Page = 1,
            PageSize = 10,
            SearchTerm = _marker,
            SortBy = "ColdRent",
            SortDescending = false
        });
        var paged = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var slice = await GetAsync(new PropertyFilterDto
            {
                Page = page,
                PageSize = 2,
                SearchTerm = _marker,
                SortBy = "ColdRent",
                SortDescending = false
            });
            paged.AddRange(slice.Items.Select(p => p.Id));
        }

        // Rents are 100/100/100/200/200/300 in seeding order p0..p5 -- heavy ties. Paging must
        // neither drop nor repeat a row, and must agree with the unpaged order.
        Assert.Equal(6, paged.Distinct().Count());
        Assert.Equal(all.Items.Select(p => p.Id), paged);
        Assert.Equal(
            all.Items.Select(p => p.ColdRent),
            all.Items.Select(p => p.ColdRent).OrderBy(r => r));
    }

    private async Task SeedAsync()
    {
        var owner = await _factory.SeedUserAsync($"{_marker}@test.local");
        var now = DateTime.UtcNow;
        decimal[] rents = [100m, 100m, 100m, 200m, 200m, 300m];

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        for (var i = 0; i < 6; i++)
        {
            var property = Property.Create(
                $"{_marker} p{i}", "وصف كافٍ للإعلان", owner.UserAccountId, ListingType.ForRent);
            property.CreatedAt = i == 4 ? now.AddDays(-10) : now.AddDays(-i);
            property.ColdRent = rents[i];
            if (i == 4) property.MarkFeatured(TimeSpan.FromDays(30), now.AddDays(-1));
            if (i == 0)
                property.Images.Add(new PropertyImage
                {
                    Url = "https://cdn.example.test/p0-main.jpg",
                    PublicId = "p0-main",
                    IsMain = true
                });
            if (i == 1)
                property.Images.Add(new PropertyImage
                {
                    Url = "https://cdn.example.test/p1-extra.jpg",
                    PublicId = "p1-extra",
                    IsMain = false
                });
            db.Properties.Add(property);
        }

        await db.SaveChangesAsync();
    }

    private async Task<PagedResult<Property>> GetAsync(PropertyFilterDto filter)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await new PropertyRepository(db).GetPagedAsync(filter, CancellationToken.None);
    }

    private static string[] Titles(PagedResult<Property> page) =>
        page.Items.Select(p => p.Title[(p.Title.LastIndexOf(' ') + 1)..]).ToArray();
}
