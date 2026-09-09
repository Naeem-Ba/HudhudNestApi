using Moq;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Listings.Queries.GetSocialPerformance;
using PropertyApi.Domain.Listings.Enums;

namespace PropertyApi.Application.Tests.Listings;

/// <summary>Phase 14 spec §10 — the Social Performance report is built purely from real Share/Attribution event aggregates, never Publication counts or estimates.</summary>
public sealed class GetSocialPerformanceQueryHandlerTests
{
    private static Mock<IPropertyShareEventRepository> MakeShares(int total = 0, params UtmSourceCount[] byPlatform)
    {
        var mock = new Mock<IPropertyShareEventRepository>();
        mock.Setup(x => x.CountAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync(total);
        mock.Setup(x => x.CountByUtmSourceAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(byPlatform);
        return mock;
    }

    private static Mock<IPropertyAttributionEventRepository> MakeAttribution(
        Dictionary<PropertyAttributionEventType, int> totals, Dictionary<PropertyAttributionEventType, UtmSourceCount[]> byPlatform)
    {
        var mock = new Mock<IPropertyAttributionEventRepository>();
        foreach (var (type, total) in totals)
        {
            mock.Setup(x => x.CountAsync(It.IsAny<Guid?>(), type, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(total);
        }

        foreach (var (type, rows) in byPlatform)
        {
            mock.Setup(x => x.CountByUtmSourceAsync(It.IsAny<Guid?>(), type, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows);
        }

        return mock;
    }

    private static Dictionary<PropertyAttributionEventType, int> DefaultTotals() => new()
    {
        [PropertyAttributionEventType.View] = 0,
        [PropertyAttributionEventType.VisitRequestCreated] = 0,
        [PropertyAttributionEventType.ContactClick] = 0,
        [PropertyAttributionEventType.PhoneClick] = 0,
        [PropertyAttributionEventType.WhatsAppClick] = 0,
        [PropertyAttributionEventType.MessageSent] = 0,
    };

    private static Dictionary<PropertyAttributionEventType, UtmSourceCount[]> DefaultByPlatform() => new()
    {
        [PropertyAttributionEventType.View] = Array.Empty<UtmSourceCount>(),
        [PropertyAttributionEventType.VisitRequestCreated] = Array.Empty<UtmSourceCount>(),
        [PropertyAttributionEventType.ContactClick] = Array.Empty<UtmSourceCount>(),
        [PropertyAttributionEventType.PhoneClick] = Array.Empty<UtmSourceCount>(),
        [PropertyAttributionEventType.WhatsAppClick] = Array.Empty<UtmSourceCount>(),
        [PropertyAttributionEventType.MessageSent] = Array.Empty<UtmSourceCount>(),
    };

    [Fact]
    public async Task Handle_ComputesTotals_AndConversionRate_FromRealEventCounts()
    {
        var totals = DefaultTotals();
        totals[PropertyAttributionEventType.View] = 100;
        totals[PropertyAttributionEventType.VisitRequestCreated] = 5;

        var shares = MakeShares(total: 40);
        var attribution = MakeAttribution(totals, DefaultByPlatform());

        var handler = new GetSocialPerformanceQueryHandler(shares.Object, attribution.Object);
        var result = await handler.Handle(new GetSocialPerformanceQuery(null, null, null), CancellationToken.None);

        Assert.Equal(40, result.Shares);
        Assert.Equal(100, result.Visits);
        Assert.Equal(5, result.Leads);
        Assert.Equal(5.0, result.ConversionRatePercent);
    }

    [Fact]
    public async Task Handle_MergesContactEventTypes_IntoSingleContactsTotal()
    {
        var totals = DefaultTotals();
        totals[PropertyAttributionEventType.ContactClick] = 2;
        totals[PropertyAttributionEventType.PhoneClick] = 3;
        totals[PropertyAttributionEventType.WhatsAppClick] = 1;
        totals[PropertyAttributionEventType.MessageSent] = 4;

        var shares = MakeShares();
        var attribution = MakeAttribution(totals, DefaultByPlatform());

        var handler = new GetSocialPerformanceQueryHandler(shares.Object, attribution.Object);
        var result = await handler.Handle(new GetSocialPerformanceQuery(null, null, null), CancellationToken.None);

        Assert.Equal(10, result.Contacts);
    }

    [Fact]
    public async Task Handle_ZeroVisits_ConversionRateIsZero_NeverDividesByZero()
    {
        var shares = MakeShares();
        var attribution = MakeAttribution(DefaultTotals(), DefaultByPlatform());

        var handler = new GetSocialPerformanceQueryHandler(shares.Object, attribution.Object);
        var result = await handler.Handle(new GetSocialPerformanceQuery(null, null, null), CancellationToken.None);

        Assert.Equal(0, result.ConversionRatePercent);
    }

    [Fact]
    public async Task Handle_BreaksDownByPlatform_UsingUtmSource()
    {
        var totals = DefaultTotals();
        totals[PropertyAttributionEventType.View] = 30;

        var byPlatform = DefaultByPlatform();
        byPlatform[PropertyAttributionEventType.View] = new[] { new UtmSourceCount("facebook", 20), new UtmSourceCount("telegram", 10) };

        var shares = MakeShares(total: 5, new UtmSourceCount("facebook", 5));
        var attribution = MakeAttribution(totals, byPlatform);

        var handler = new GetSocialPerformanceQueryHandler(shares.Object, attribution.Object);
        var result = await handler.Handle(new GetSocialPerformanceQuery(null, null, null), CancellationToken.None);

        Assert.Contains(result.ByPlatform, p => p.Platform == "facebook" && p.Visits == 20 && p.Shares == 5);
        Assert.Contains(result.ByPlatform, p => p.Platform == "telegram" && p.Visits == 10);
    }
}
