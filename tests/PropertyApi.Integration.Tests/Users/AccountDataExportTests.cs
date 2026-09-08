using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Integration.Tests.Investments;

namespace PropertyApi.Integration.Tests.Users;

/// <summary>
/// Real HTTP + real-Postgres regression coverage for Finding F7's export endpoint
/// (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md): the export must contain the caller's own
/// data, must never contain a second seeded user's data (the actual IDOR proof), and must never
/// contain a password-hash-shaped value or the raw JSON payload text "PasswordHash".
/// </summary>
public sealed class AccountDataExportTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "GET /api/Users/me/export returns only the caller's own data, never another seeded user's")]
    public async Task Export_ReturnsOwnDataOnly_NeverAnotherUsersData()
    {
        var owner = await _factory.SeedUserAsync("export-owner");
        var otherUser = await _factory.SeedUserAsync("export-other");

        var propertyId = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = Property.Create(
                "Owner's Exported Property", "Integration test property", owner.Id, ListingType.ForSale);
            db.Properties.Add(property);
            await db.SaveChangesAsync();
            return property.Id;
        });

        // A property belonging to the OTHER seeded user -- must never appear in owner's export.
        var otherPropertyId = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = Property.Create(
                "Other User's Property", "Should never appear in owner's export", otherUser.Id, ListingType.ForRent);
            db.Properties.Add(property);
            await db.SaveChangesAsync();
            return property.Id;
        });

        using var client = _factory.AuthedClient(owner.AccessToken);
        var response = await client.GetAsync("/api/Users/me/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();

        // Never leaks a secret field, regardless of what future fields get added to the DTO.
        Assert.DoesNotContain("PasswordHash", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecurityStamp", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(otherUser.Email, rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Other User's Property", rawJson, StringComparison.Ordinal);

        var export = await response.Content.ReadFromJsonAsync<ExportEnvelope>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(export);
        Assert.Equal(owner.Id, export!.Account.Id);
        Assert.Contains(export.Properties, p => p.Id == propertyId);
        Assert.DoesNotContain(export.Properties, p => p.Id == otherPropertyId);
    }

    [Fact(DisplayName = "GET /api/Users/me/export requires authentication")]
    public async Task Export_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.AuthedClient();
        var response = await client.GetAsync("/api/Users/me/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Minimal shape for deserialization -- deliberately not the full production DTO, to keep
    // this test resilient to additive fields.
    private sealed class ExportEnvelope
    {
        public ExportAccount Account { get; set; } = new();
        public List<ExportProperty> Properties { get; set; } = new();
    }

    private sealed class ExportAccount
    {
        public Guid Id { get; set; }
    }

    private sealed class ExportProperty
    {
        public Guid Id { get; set; }
    }
}
