using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Services;

/// <summary>
/// QA Phase 11 (services marketplace) regression tests over real HTTP and real Postgres: values the
/// validators used to accept and the database then rejected (500), an offering that stayed public
/// after it was switched off, and a request opened on somebody else's unpublished listing.
/// </summary>
public sealed class ServicePhase11FixTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();
    private int _damascus;
    private int _currency;

    private SeededUser _admin = null!;
    private SeededUser _provider = null!;
    private SeededUser _customer = null!;
    private SeededUser _stranger = null!;
    private Guid _offering;
    private Guid _providerId;
    private HttpClient AdminClient => _factory.AuthedClient(_admin.AccessToken);
    private HttpClient ProviderClient => _factory.AuthedClient(_provider.AccessToken);
    private HttpClient CustomerClient => _factory.AuthedClient(_customer.AccessToken);
    private HttpClient StrangerClient => _factory.AuthedClient(_stranger.AccessToken);

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        (_damascus, _currency) = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var damascus = Governorate.Create("دمشق", "Damascus", "SY", 1);
            db.Governorates.Add(damascus);
            await db.SaveChangesAsync();
            var currency = Currency.Create("USD", "US Dollar", "الدولار", "$", 1m, isBaseCurrency: true);
            db.Currencies.Add(currency);
            await db.SaveChangesAsync();
            return (damascus.Id, currency.Id);
        });

        _admin = await _factory.SeedUserAsync("admin", "Admin");
        _provider = await _factory.SeedUserAsync("provider");
        _customer = await _factory.SeedUserAsync("customer");
        _stranger = await _factory.SeedUserAsync("stranger");

        var created = await ReadJson(await AdminClient.PostAsJsonAsync("/api/ServiceProviders", new
        {
            userId = _provider.Id,
            displayName = "Office",
            bio = "bio",
            contactEmail = "office@example.test",
            contactPhone = "+963 11 555 1234",
            approvedCategories = new[] { 3 },
            coverageGovernorateIds = new[] { _damascus },
        }), HttpStatusCode.Created);
        _providerId = created.GetProperty("id").GetGuid();

        var offering = await ReadJson(await ProviderClient.PostAsJsonAsync("/api/ServiceOfferings",
            new { category = 3, title = "Inspection", description = "d", basePrice = 100, currencyId = _currency, estimatedDurationDays = 2 }),
            HttpStatusCode.Created);
        _offering = offering.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private Task<Guid> NewPropertyAsync(Guid ownerId, bool published = true) => _factory.InScopeAsync(async services =>
    {
        var id = await _factory.SeedPropertyAsync(ownerId);
        var db = services.GetRequiredService<AppDbContext>();
        await db.Properties.Where(p => p.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.GovernorateId, _damascus)
            .SetProperty(p => p.IsPublished, published));
        return id;
    });

    private async Task<Guid> ConfirmedRequestAsync()
    {
        var created = await ReadJson(await CustomerClient.PostAsJsonAsync("/api/ServiceRequests", new
        {
            propertyId = await NewPropertyAsync(_customer.Id),
            serviceOfferingId = _offering,
            requesterNote = "hello",
            intent = "Request",
        }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        await ReadJson(await CustomerClient.PutAsync($"/api/ServiceRequests/{id}/confirm", null), HttpStatusCode.OK);
        return id;
    }

    // ── P11-D03 / D04: values that failed at the database ───────────────

    [Fact(DisplayName = "P11-D03: an unknown currency answers 422 on offering create, update, accept and complete")]
    public async Task UnknownCurrency_Is422_EverywhereItIsAccepted()
    {
        const int unknown = 9999;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ProviderClient.PostAsJsonAsync("/api/ServiceOfferings",
            new { category = 3, title = "Other", basePrice = 10, currencyId = unknown })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ProviderClient.PutAsJsonAsync($"/api/ServiceOfferings/{_offering}",
            new { title = "Inspection", description = "d", basePrice = 10, currencyId = unknown, estimatedDurationDays = 2, isActive = true })).StatusCode);

        var request = await ConfirmedRequestAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/accept",
            new { quotedPrice = 100, quotedPriceCurrencyId = unknown })).StatusCode);

        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/accept",
            new { quotedPrice = 100, quotedPriceCurrencyId = _currency }), HttpStatusCode.OK);
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/schedule",
            new { scheduledAt = DateTime.UtcNow.AddDays(2) }), HttpStatusCode.OK);
        await ReadJson(await ProviderClient.PutAsync($"/api/ServiceRequests/{request}/start", null), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/complete",
            new { finalPrice = 100, finalPriceCurrencyId = unknown })).StatusCode);
    }

    [Fact(DisplayName = "P11-D04/D05: overlong or out-of-range values answer 422 instead of 500")]
    public async Task OverlongAndOutOfRange_Values_Are422()
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ProviderClient.PutAsJsonAsync("/api/ServiceProviders/me", new
        {
            displayName = "Office",
            contactEmail = new string('a', 320) + "@example.test",
        })).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ProviderClient.PostAsJsonAsync("/api/ServiceOfferings",
            new { category = 3, title = "Long", estimatedDurationDays = 2_000_000_000 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ProviderClient.PostAsJsonAsync("/api/ServiceOfferings",
            new { category = 3, title = "Pricey", basePrice = 1_000_000_000_000m, currencyId = _currency })).StatusCode);

        var request = await ConfirmedRequestAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/accept",
            new { providerNote = new string('x', 100_000) })).StatusCode);
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/accept",
            new { providerNote = new string('x', 1000) }), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/schedule",
            new { scheduledAt = new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc) })).StatusCode);
    }

    [Fact(DisplayName = "P11-D05: the administrator cannot create a provider for an unknown user or set a level that does not exist")]
    public async Task Admin_UnknownUser_And_UnknownLevel()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await AdminClient.PostAsJsonAsync("/api/ServiceProviders",
            new { userId = Guid.NewGuid(), displayName = "Ghost" })).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await AdminClient.PutAsJsonAsync(
            $"/api/ServiceProviders/{_providerId}/verification", new { level = 99 })).StatusCode);
    }

    // ── P11-D01 / D02: the anonymous valuation endpoints ────────────────

    [Fact(DisplayName = "P11-D02: an anonymous valuation request with ids that do not exist or absurd sizes answers 422, a valid one 201")]
    public async Task Valuation_Create_RejectsUnknownIdsAndAbsurdSizes()
    {
        var anonymous = _factory.CreateClient();
        object Body(int governorate = 0, int? propertyType = null, decimal area = 90m, int rooms = 3) => new
        {
            governorateId = governorate == 0 ? _damascus : governorate,
            propertyTypeId = propertyType,
            area,
            rooms,
            requestType = 1,
        };

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.PostAsJsonAsync("/api/valuationinquiries", Body(governorate: 9999))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.PostAsJsonAsync("/api/valuationinquiries", Body(propertyType: 9999))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.PostAsJsonAsync("/api/valuationinquiries", Body(area: 79228162514264337593543950335m))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.PostAsJsonAsync("/api/valuationinquiries", Body(rooms: int.MaxValue))).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await anonymous.PostAsJsonAsync("/api/valuationinquiries", Body())).StatusCode);
    }

    [Fact(DisplayName = "P11-D01: a malformed contact phone or email on the consent is a 422 before anything is stored")]
    public async Task Valuation_Consent_RejectsMalformedContact()
    {
        var anonymous = _factory.CreateClient();
        var route = $"/api/valuationinquiries/{Guid.NewGuid()}/consent";

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.PostAsJsonAsync(route,
            new { invitationId = Guid.NewGuid(), contactPhone = "abc" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.PostAsJsonAsync(route,
            new { invitationId = Guid.NewGuid(), contactEmail = new string('a', 490) + "@x.test" })).StatusCode);
    }

    // ── P11-D06: two reviews racing ─────────────────────────────────────

    [Fact(DisplayName = "P11-D06: the second review of one request is a 409, not a 500")]
    public async Task SecondReview_SameRequest_IsConflict()
    {
        var request = await ConfirmedRequestAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/accept", new { }), HttpStatusCode.OK);
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/schedule",
            new { scheduledAt = DateTime.UtcNow.AddDays(2) }), HttpStatusCode.OK);
        await ReadJson(await ProviderClient.PutAsync($"/api/ServiceRequests/{request}/start", null), HttpStatusCode.OK);
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/complete", new { }), HttpStatusCode.OK);
        await ReadJson(await CustomerClient.PostAsJsonAsync($"/api/ServiceRequests/{request}/review", new { rating = 4, comment = "good" }),
            HttpStatusCode.Created);

        // The handler's own "already reviewed" check is what a sequential second call meets. The unique
        // index is what two simultaneous calls meet, so the write is made past the handler.
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.ServiceReviews.AddAsync(
                ServiceReview.Create(request, _providerId, _customer.Id, 5, "again", DateTime.UtcNow));
            var uow = services.GetRequiredService<IUnitOfWork>();
            await Assert.ThrowsAsync<ConflictException>(() => uow.SaveChangesAsync());
        });
    }

    // ── P11-D08: switched-off offerings are not public ──────────────────

    [Fact(DisplayName = "P11-D08: an offering switched off, or whose provider is suspended, is a 404 by id and cannot be re-enabled by a suspended provider")]
    public async Task InactiveOrSuspended_Offering_IsNotPublic()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/ServiceOfferings/{_offering}")).StatusCode);

        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceOfferings/{_offering}",
            new { title = "Inspection", description = "d", basePrice = 100, currencyId = _currency, estimatedDurationDays = 2, isActive = false }),
            HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/ServiceOfferings/{_offering}")).StatusCode);

        await ReadJson(await AdminClient.PostAsJsonAsync($"/api/admin/service-providers/{_providerId}/suspend", new { reason = "qa" }),
            HttpStatusCode.OK);

        // Re-enabling and creating are refused while suspended; switching off stays allowed.
        Assert.Equal(HttpStatusCode.Conflict, (await ProviderClient.PutAsJsonAsync($"/api/ServiceOfferings/{_offering}",
            new { title = "Inspection", description = "d", basePrice = 100, currencyId = _currency, estimatedDurationDays = 2, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ProviderClient.PostAsJsonAsync("/api/ServiceOfferings",
            new { category = 3, title = "While suspended" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/ServiceOfferings/{_offering}")).StatusCode);
    }

    // ── P11-D09 / D10 ───────────────────────────────────────────────────

    [Fact(DisplayName = "P11-D09: a request on somebody else's unpublished listing is a 404; the owner's own is allowed")]
    public async Task Request_OnOthersUnpublishedProperty_IsNotFound()
    {
        var draft = await NewPropertyAsync(_stranger.Id, published: false);

        Assert.Equal(HttpStatusCode.NotFound, (await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = draft, serviceOfferingId = _offering, requesterNote = "hi", intent = "Inquire" })).StatusCode);

        await ReadJson(await StrangerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = draft, serviceOfferingId = _offering, requesterNote = "hi", intent = "Inquire" }), HttpStatusCode.Created);
    }

    [Fact(DisplayName = "P11-D10: a rejected request takes no documents")]
    public async Task Documents_OnRejectedRequest_AreConflict()
    {
        var request = await ConfirmedRequestAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{request}/reject", new { reason = "busy" }), HttpStatusCode.OK);

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "a.png");

        var response = await CustomerClient.PostAsync($"/api/ServiceRequests/{request}/documents", form);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
