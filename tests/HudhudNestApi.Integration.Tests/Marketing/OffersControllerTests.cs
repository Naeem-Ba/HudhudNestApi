using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;
using HudhudNestApi.Domain.Marketing.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.TestInfrastructure;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Marketing;

/// <summary>
/// Every test class below gets its OWN <see cref="TestApplication"/> instance (and
/// therefore its own fresh EF Core InMemory database — see TestApplication's per-instance
/// database name) — deliberately one offer-creating scenario per class, never two sharing
/// an <c>IClassFixture</c>. <c>GetActive</c> returns "the" single current offer with no id
/// filter, so two Active offers seeded by unrelated tests sharing one database would make
/// the result non-deterministic.
/// </summary>
public sealed class OffersController_WhenNoOfferExists_Tests : IClassFixture<TestApplication>
{
    private readonly HttpClient _client;

    public OffersController_WhenNoOfferExists_Tests(TestApplication factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetActive_WithNoOffersAtAll_Returns204()
    {
        var response = await _client.GetAsync("/api/offers/active");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}

public sealed class OffersController_WithActiveOfferInWindow_Tests : IClassFixture<TestApplication>
{
    private readonly TestApplication _factory;
    private readonly HttpClient _client;

    public OffersController_WithActiveOfferInWindow_Tests(TestApplication factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetActive_WithActiveOfferWithinWindow_ReturnsIt()
    {
        var offer = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var seeded = Offer.Create(
                "أول 100 مكتب",
                OfferDiscountType.Percentage,
                20m,
                startsAtUtc: DateTime.UtcNow.AddDays(-1),
                endsAtUtc: DateTime.UtcNow.AddDays(30),
                maxRedemptions: 100);
            seeded.Activate();
            db.Offers.Add(seeded);
            await db.SaveChangesAsync();
            return seeded;
        });

        var response = await _client.GetAsync("/api/offers/active");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OfferResponse>();
        Assert.NotNull(body);
        Assert.Equal(offer.Id, body!.Id);
        Assert.Equal(0, body.RedeemedCount);
        Assert.Equal(100, body.MaxRedemptions);
    }

    private sealed record OfferResponse(
        Guid Id,
        string Name,
        string? Description,
        string DiscountType,
        decimal DiscountValue,
        string? TargetPlanTier,
        DateTime? EndsAtUtc,
        int? MaxRedemptions,
        int RedeemedCount,
        string? Terms);
}

/// <summary>"لا يظهر عرض منتهي" — an offer past its EndsAtUtc must disappear from the
/// public surface on its own, without any admin action.</summary>
public sealed class OffersController_WithExpiredOffer_Tests : IClassFixture<TestApplication>
{
    private readonly TestApplication _factory;
    private readonly HttpClient _client;

    public OffersController_WithExpiredOffer_Tests(TestApplication factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetActive_WithOfferPastEndDate_Returns204()
    {
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var offer = Offer.Create(
                "عرض منتهٍ",
                OfferDiscountType.Percentage,
                20m,
                startsAtUtc: DateTime.UtcNow.AddDays(-30),
                endsAtUtc: DateTime.UtcNow.AddDays(-1));
            offer.Activate();
            db.Offers.Add(offer);
            await db.SaveChangesAsync();
        });

        var response = await _client.GetAsync("/api/offers/active");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}

/// <summary>
/// The core "first 100 agencies" guarantee, exercised through the real HTTP submission
/// flow: the redemption counter must never exceed MaxRedemptions no matter how many
/// submissions arrive after the cap is reached, and a fully redeemed offer must disappear
/// from the public surface immediately. Uses <see cref="InMemorySafeOfferRepository"/> in
/// place of the real Postgres-only atomic-UPDATE repository — see that class's doc comment
/// for why, and for what this substitution does and does not prove.
/// </summary>
public sealed class OffersController_RedemptionCapTests : IClassFixture<TestApplication>
{
    private readonly TestApplication _factory;
    private readonly HttpClient _client;

    public OffersController_RedemptionCapTests(TestApplication factory)
    {
        _factory = factory;

        var clientFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IOfferRepository, InMemorySafeOfferRepository>()));
        _client = clientFactory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Submit_MultipleLeadsAgainstOneSlotOffer_OnlyFirstClaimsIt()
    {
        var offerId = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var offer = Offer.Create(
                "أول مكتب واحد",
                OfferDiscountType.Percentage,
                30m,
                startsAtUtc: DateTime.UtcNow.AddDays(-1),
                maxRedemptions: 1);
            offer.Activate();
            db.Offers.Add(offer);
            await db.SaveChangesAsync();
            return offer.Id;
        });

        var first = await SubmitLeadAsync(offerId, "مكتب الأول");
        var second = await SubmitLeadAsync(offerId, "مكتب الثاني");
        var third = await SubmitLeadAsync(offerId, "مكتب الثالث");

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.True(first.Body!.OfferApplied);

        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.False(second.Body!.OfferApplied);

        Assert.Equal(HttpStatusCode.OK, third.Status);
        Assert.False(third.Body!.OfferApplied);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            // All three leads were still recorded — a full offer never blocks a genuine signup.
            Assert.Equal(3, await db.Leads.CountAsync());

            var offer = await db.Offers.FindAsync(offerId);
            Assert.NotNull(offer);
            Assert.Equal(1, offer!.RedeemedCount); // never exceeds MaxRedemptions
        });

        // Once fully redeemed, the offer must vanish from the public surface immediately —
        // no admin action (Status change) needed.
        var afterFull = await _client.GetAsync("/api/offers/active");
        Assert.Equal(HttpStatusCode.NoContent, afterFull.StatusCode);
    }

    private async Task<(HttpStatusCode Status, SubmitLeadResponse? Body)> SubmitLeadAsync(Guid offerId, string name)
    {
        var response = await _client.PostAsJsonAsync("/api/leads", new
        {
            FullName = name,
            Phone = "0933123456",
            City = "دمشق",
            UserType = "agency",
            OfferId = offerId
        });

        var body = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SubmitLeadResponse>()
            : null;

        return (response.StatusCode, body);
    }

    private sealed record SubmitLeadResponse(Guid LeadId, bool OfferRequested, bool OfferApplied);
}
