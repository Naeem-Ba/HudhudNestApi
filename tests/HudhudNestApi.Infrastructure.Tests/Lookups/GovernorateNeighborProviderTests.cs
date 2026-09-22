using Moq;
using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Infrastructure.Lookups;

namespace HudhudNestApi.Infrastructure.Tests.Lookups;

/// <summary>
/// Stage 4 (Valuation Office Matching) Level 4 fallback — GovernorateNeighborProvider's own
/// id-resolution logic (NameEn ↔ id lookup and the symmetric border map), independent of
/// OfficeMatchingService's level-by-level orchestration (covered separately, mocked, in
/// HudhudNestApi.Application.Tests).
/// </summary>
public sealed class GovernorateNeighborProviderTests
{
    // Mirrors the real 14 Syrian governorates seeded by GovernoratesSeed.cs (ids arbitrary —
    // only NameEn needs to match, since that's what BorderMap is keyed by).
    private static readonly StructuredLocationLookupDto[] Governorates =
    [
        new(1, "دمشق", "Damascus"),
        new(2, "ريف دمشق", "Rural Damascus"),
        new(3, "حلب", "Aleppo"),
        new(4, "حمص", "Homs"),
        new(5, "حماة", "Hama"),
        new(6, "اللاذقية", "Latakia"),
        new(7, "طرطوس", "Tartus"),
        new(8, "الحسكة", "Al-Hasakah"),
        new(9, "دير الزور", "Deir ez-Zor"),
        new(10, "الرقة", "Raqqa"),
        new(11, "إدلب", "Idlib"),
        new(12, "درعا", "Daraa"),
        new(13, "القنيطرة", "Quneitra"),
        new(14, "السويداء", "As-Suwayda"),
    ];

    [Fact]
    public async Task Damascus_BordersOnlyRuralDamascus()
    {
        var provider = Build();

        var neighborIds = await provider.GetNeighborIdsAsync(governorateId: 1);

        Assert.Equal([2], neighborIds);
    }

    [Fact]
    public async Task RuralDamascus_BordersDamascusAndFourOthers()
    {
        var provider = Build();

        var neighborIds = await provider.GetNeighborIdsAsync(governorateId: 2);

        // Damascus(1), Homs(4), Quneitra(13), Daraa(12), As-Suwayda(14).
        Assert.Equal(new[] { 1, 4, 12, 13, 14 }, neighborIds.OrderBy(x => x));
    }

    [Fact]
    public async Task BorderMap_IsSymmetric_ADeclaredNeighborAlwaysListsTheOriginalBack()
    {
        var provider = Build();

        foreach (var governorate in Governorates)
        {
            var neighborIds = await provider.GetNeighborIdsAsync(governorate.Id);

            foreach (var neighborId in neighborIds)
            {
                var reverseNeighbors = await provider.GetNeighborIdsAsync(neighborId);
                Assert.Contains(governorate.Id, reverseNeighbors);
            }
        }
    }

    [Fact]
    public async Task NeverIncludes_TheGovernorateItself()
    {
        var provider = Build();

        foreach (var governorate in Governorates)
        {
            var neighborIds = await provider.GetNeighborIdsAsync(governorate.Id);
            Assert.DoesNotContain(governorate.Id, neighborIds);
        }
    }

    [Fact]
    public async Task UnknownGovernorateId_ReturnsEmpty_NeverThrows()
    {
        var provider = Build();

        var neighborIds = await provider.GetNeighborIdsAsync(governorateId: 999);

        Assert.Empty(neighborIds);
    }

    private static GovernorateNeighborProvider Build()
    {
        var lookups = new Mock<ICommonLookupService>();
        lookups
            .Setup(x => x.GetGovernoratesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Governorates);

        return new GovernorateNeighborProvider(lookups.Object);
    }
}
