using Moq;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Listings.Queries.GetPropertiesList;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Properties.Validators;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Users.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>The caller's interest areas: parsing, validation and the "in your area" flag on list items.</summary>
public sealed class InterestGovernorateFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingGiven_isNoInterest(string? raw)
    {
        Assert.True(PropertyFilterDto.TryParseInterestGovernorateIds(raw, out var ids));
        Assert.Empty(ids);
    }

    [Theory]
    [InlineData("3", new[] { 3 })]
    [InlineData("7,3", new[] { 3, 7 })]          // a set: order does not matter
    [InlineData("3,3,7", new[] { 3, 7 })]         // duplicates collapse
    [InlineData(" 12 , 4 ,9", new[] { 4, 9, 12 })]
    public void ValidLists_becomeASortedSet(string raw, int[] expected)
    {
        Assert.True(PropertyFilterDto.TryParseInterestGovernorateIds(raw, out var ids));
        Assert.Equal(expected, ids);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("abc")]
    [InlineData("3,,7")]
    [InlineData("3;7")]
    [InlineData("1.5")]
    [InlineData("+3")]
    [InlineData("1,2,3,4")]                       // at most three
    [InlineData("99999999999")]
    public void MalformedLists_areRejected(string raw)
    {
        Assert.False(PropertyFilterDto.TryParseInterestGovernorateIds(raw, out _));
        Assert.Empty(new PropertyFilterDto { InterestGovernorateIds = raw }.ParseInterestGovernorateIds());
    }

    [Fact]
    public void TheValidator_answersMalformedLists_andAcceptsGoodOnes()
    {
        var validator = new PropertyFilterDtoValidator();

        Assert.True(validator.Validate(new PropertyFilterDto { InterestGovernorateIds = "3,7" }).IsValid);
        Assert.True(validator.Validate(new PropertyFilterDto { InterestGovernorateIds = null }).IsValid);

        var bad = validator.Validate(new PropertyFilterDto { InterestGovernorateIds = "1,2,3,4" });
        Assert.False(bad.IsValid);
        Assert.Contains(bad.Errors, e => e.PropertyName == nameof(PropertyFilterDto.InterestGovernorateIds));
    }

    private static Property Listing(int? governorateId, bool featured)
    {
        var p = Property.Create("Flat", "A long enough description", Guid.NewGuid(), ListingType.ForSale, currencyCode: "USD");
        p.GovernorateId = governorateId;
        if (featured) p.MarkFeatured(TimeSpan.FromDays(10), DateTime.UtcNow);
        return p;
    }

    [Fact]
    public async Task OnlyAFeaturedListingInAnInterestGovernorate_isFlagged()
    {
        var inArea = Listing(3, featured: true);
        var otherAd = Listing(5, featured: true);
        var plainInArea = Listing(3, featured: false);
        var noGovernorate = Listing(null, featured: true);

        var repo = new Mock<IPropertyRepository>();
        repo.Setup(r => r.GetPagedWithViewCountsAsync(It.IsAny<PropertyFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new PagedResult<Property>
            {
                Items = new[] { inArea, otherAd, plainInArea, noGovernorate },
                TotalCount = 4,
                Page = 1,
                PageSize = 20
            }, (IReadOnlyDictionary<Guid, long>)new Dictionary<Guid, long>()));

        var result = await new GetPropertiesListQueryHandler(repo.Object).Handle(
            new GetPropertiesListQuery(new PropertyFilterDto { InterestGovernorateIds = "3,7" }), CancellationToken.None);

        Assert.Equal([true, false, false, false], result.Items.Select(i => i.MatchesInterestArea));
    }

    [Fact]
    public async Task WithoutInterestAreas_nothingIsFlagged()
    {
        var repo = new Mock<IPropertyRepository>();
        repo.Setup(r => r.GetPagedWithViewCountsAsync(It.IsAny<PropertyFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new PagedResult<Property>
            {
                Items = new[] { Listing(3, featured: true) },
                TotalCount = 1,
                Page = 1,
                PageSize = 20
            }, (IReadOnlyDictionary<Guid, long>)new Dictionary<Guid, long>()));

        var result = await new GetPropertiesListQueryHandler(repo.Object)
            .Handle(new GetPropertiesListQuery(new PropertyFilterDto()), CancellationToken.None);

        Assert.All(result.Items, i => Assert.False(i.MatchesInterestArea));
    }

    [Fact]
    public void TheHomeGovernorate_canBeSetAndCleared_andNeverHoldsANonPositiveId()
    {
        var now = new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
        var account = UserAccount.Create(Guid.NewGuid(), "Ada", "Lovelace", now);
        Assert.Null(account.HomeGovernorateId);

        account.SetHomeGovernorate(6, now);
        Assert.Equal(6, account.HomeGovernorateId);

        account.SetHomeGovernorate(0, now.AddMinutes(1));
        Assert.Null(account.HomeGovernorateId);

        account.SetHomeGovernorate(-3, now.AddMinutes(2));
        Assert.Null(account.HomeGovernorateId);
    }
}
