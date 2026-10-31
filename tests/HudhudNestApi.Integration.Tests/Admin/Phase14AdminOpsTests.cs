using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Admin;

/// <summary>
/// QA phase 14 fixes over real HTTP and real Postgres: the last-administrator guard, disable/enable of an
/// account together with its listings, the all-or-nothing approval of a location suggestion, the audit rows
/// of admin actions that had none, the masked-IP audit log read endpoint, and the 409 an admin race returns.
/// </summary>
public sealed class Phase14AdminOpsTests : IAsyncLifetime
{
    private const string Admin = "Admin";
    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<int> EnsureGovernorateAsync(AppDbContext db)
    {
        var existing = await db.Governorates.OrderBy(g => g.Id).FirstOrDefaultAsync();
        if (existing is not null)
            return existing.Id;

        var created = Governorate.Create("محافظة اختبار", "Test Governorate");
        db.Governorates.Add(created);
        await db.SaveChangesAsync();
        return created.Id;
    }

    [Fact(DisplayName = "D01: a sole administrator cannot remove or demote their own Admin role")]
    public async Task SoleAdminCannotRemoveOwnAdminRole()
    {
        var admin = await _factory.SeedUserAsync("solo", Admin);
        var client = _factory.AuthedClient(admin.AccessToken);

        await Json(await client.DeleteAsync($"/api/admin/users/{admin.Id}/roles/Admin"), HttpStatusCode.Conflict);
        await Json(await client.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { role = "User" }), HttpStatusCode.Conflict);

        var users = await Json(await client.GetAsync($"/api/admin/users?role=Admin"), HttpStatusCode.OK);
        Assert.Equal(1, users.GetProperty("total").GetInt32());
    }

    [Fact(DisplayName = "D01: with two administrators one can demote the other, and the last one then cannot be removed")]
    public async Task LastAdminIsProtected()
    {
        var first = await _factory.SeedUserAsync("first", Admin);
        var second = await _factory.SeedUserAsync("second", Admin);
        var client = _factory.AuthedClient(first.AccessToken);

        await Json(await client.DeleteAsync($"/api/admin/users/{second.Id}/roles/Admin"), HttpStatusCode.OK);

        // 'second' lost the role, so 'first' is the only administrator left; nobody can take it away.
        var third = await _factory.SeedUserAsync("third", Admin);
        var thirdClient = _factory.AuthedClient(third.AccessToken);
        await Json(await thirdClient.DeleteAsync($"/api/admin/users/{first.Id}/roles/Admin"), HttpStatusCode.OK); // 2 admins -> 1: allowed
        await Json(await thirdClient.DeleteAsync($"/api/admin/users/{third.Id}/roles/Admin"), HttpStatusCode.Conflict); // would leave none
    }

    [Fact(DisplayName = "D10: removing a user's only role leaves them the base User role; unknown filters are rejected")]
    public async Task RoleFallbackAndFilterValidation()
    {
        var admin = await _factory.SeedUserAsync("admin", Admin);
        var agent = await _factory.SeedUserAsync("agent", "Agent");
        var client = _factory.AuthedClient(admin.AccessToken);

        await Json(await client.DeleteAsync($"/api/admin/users/{agent.Id}/roles/Agent"), HttpStatusCode.OK);
        var detail = await Json(await client.GetAsync($"/api/admin/users/{agent.Id}"), HttpStatusCode.OK);
        Assert.Contains(detail.GetProperty("roles").EnumerateArray(), r => r.GetString() == "User");

        await Json(await client.GetAsync("/api/admin/users?role=bogus"), HttpStatusCode.BadRequest);
        await Json(await client.GetAsync("/api/admin/users?accountStatus=bogus"), HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "D02/D03: disabling hides the owner's listings, the user stays visible to admins, enabling restores exactly those")]
    public async Task DisableHidesListingsAndEnableRestoresThem()
    {
        var admin = await _factory.SeedUserAsync("admin", Admin);
        var owner = await _factory.SeedUserAsync("owner");
        var client = _factory.AuthedClient(admin.AccessToken);
        var liveId = await _factory.SeedPropertyAsync(owner.Id);
        var takenDownId = await _factory.SeedPropertyAsync(owner.Id);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            foreach (var id in new[] { liveId, takenDownId })
            {
                var property = await db.Properties.SingleAsync(p => p.Id == id);
                if (!property.IsPublished)
                    typeof(Property).GetProperty(nameof(Property.IsPublished))!.SetValue(property, true);
            }

            // One listing an admin had already taken down for another reason before the disable.
            (await db.Properties.SingleAsync(p => p.Id == takenDownId)).TakeDown(admin.Id, "Fake photos");
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/admin/users/{owner.Id}")).StatusCode);

        // Visible to admins as Disabled (it used to vanish from the list and the detail, with no way back).
        var disabled = await Json(await client.GetAsync($"/api/admin/users/{owner.Id}"), HttpStatusCode.OK);
        Assert.Equal("Disabled", disabled.GetProperty("accountStatus").GetString());
        var listed = await Json(await client.GetAsync("/api/admin/users?accountStatus=Disabled"), HttpStatusCode.OK);
        Assert.Equal(1, listed.GetProperty("total").GetInt32());

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var live = await db.Properties.SingleAsync(p => p.Id == liveId);
            Assert.True(live.IsTakenDown);
            Assert.False(live.IsPublished);
            Assert.Equal(Property.AccountDisabledTakeDownReason, live.TakenDownReason);
        });

        // A reason is required, and an active account cannot be "re-enabled".
        await Json(await client.PostAsJsonAsync($"/api/admin/users/{owner.Id}/enable", new { reason = "" }), HttpStatusCode.BadRequest);
        await Json(await client.PostAsJsonAsync($"/api/admin/users/{owner.Id}/enable", new { reason = "Mistaken disable" }), HttpStatusCode.OK);
        await Json(await client.PostAsJsonAsync($"/api/admin/users/{owner.Id}/enable", new { reason = "again" }), HttpStatusCode.Conflict);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var live = await db.Properties.SingleAsync(p => p.Id == liveId);
            Assert.False(live.IsTakenDown);
            Assert.True(live.IsPublished);

            // The listing an admin took down earlier for a different reason stays down.
            var other = await db.Properties.SingleAsync(p => p.Id == takenDownId);
            Assert.True(other.IsTakenDown);
            Assert.Equal("Fake photos", other.TakenDownReason);

            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "UserEnabled" && a.UserId == admin.Id));
        });
    }

    [Fact(DisplayName = "D03: an account the owner deleted (anonymised) cannot be re-enabled by an admin")]
    public async Task SelfDeletedAccountCannotBeReenabled()
    {
        var admin = await _factory.SeedUserAsync("admin", Admin);
        var gone = await _factory.SeedUserAsync("gone");

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var identity = await db.Users.SingleAsync(u => u.Id == gone.Id);
            identity.IsDeleted = true;
            identity.DeletedAt = DateTime.UtcNow;
            (await db.UserAccounts.SingleAsync(a => a.Id == gone.Id)).Anonymize(DateTime.UtcNow);
            await db.SaveChangesAsync();
        });

        await Json(
            await _factory.AuthedClient(admin.AccessToken).PostAsJsonAsync($"/api/admin/users/{gone.Id}/enable", new { reason = "x" }),
            HttpStatusCode.Conflict);
    }

    [Fact(DisplayName = "D04: approving a suggestion is all-or-nothing, reuses an existing name and cannot be repeated")]
    public async Task LocationSuggestionApprovalIsAtomic()
    {
        var admin = await _factory.SeedUserAsync("admin", Admin);
        var submitter = await _factory.SeedUserAsync("submitter");
        var client = _factory.AuthedClient(admin.AccessToken);

        var existingName = 0;
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var governorateId = await EnsureGovernorateAsync(db);
            var seededDistrict = await db.Districts.Where(d => d.GovernorateId == governorateId).OrderBy(d => d.Id).FirstOrDefaultAsync();
            if (seededDistrict is null)
            {
                seededDistrict = HudhudNestApi.Domain.Lookups.Entities.District.Create(governorateId, "Seeded", "Seeded", 1);
                db.Districts.Add(seededDistrict);
                await db.SaveChangesAsync();
            }

            db.Set<LocationSuggestion>().Add(LocationSuggestion.Create(LocationSuggestionType.District, governorateId, "New Test District", submitter.Id, null));
            db.Set<LocationSuggestion>().Add(LocationSuggestion.Create(LocationSuggestionType.District, governorateId, seededDistrict.NameEn.ToUpperInvariant(), submitter.Id, null));
            await db.SaveChangesAsync();
            existingName = seededDistrict.Id;
        });

        var pending = await Json(await client.GetAsync("/api/admin/location-suggestions"), HttpStatusCode.OK);
        var newSuggestion = pending.EnumerateArray().First(s => s.GetProperty("name").GetString() == "New Test District").GetProperty("id").GetGuid();
        var duplicateSuggestion = pending.EnumerateArray().First(s => s.GetProperty("name").GetString() != "New Test District").GetProperty("id").GetGuid();

        var districtsBefore = await _factory.InScopeAsync(s => s.GetRequiredService<AppDbContext>().Districts.CountAsync());

        // Notes too long: refused up front, nothing created.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/api/admin/location-suggestions/{newSuggestion}/approve", new { notes = new string('n', 600) })).StatusCode);
        Assert.Equal(districtsBefore, await _factory.InScopeAsync(s => s.GetRequiredService<AppDbContext>().Districts.CountAsync()));

        // Approved once: exactly one new row. A second approval is refused and creates nothing.
        await Json(await client.PostAsJsonAsync($"/api/admin/location-suggestions/{newSuggestion}/approve", new { notes = "ok" }), HttpStatusCode.OK);
        Assert.Equal(districtsBefore + 1, await _factory.InScopeAsync(s => s.GetRequiredService<AppDbContext>().Districts.CountAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/api/admin/location-suggestions/{newSuggestion}/approve", new { notes = "again" })).StatusCode);
        Assert.Equal(districtsBefore + 1, await _factory.InScopeAsync(s => s.GetRequiredService<AppDbContext>().Districts.CountAsync()));

        // A suggestion whose name already exists (any letter case) points at the existing row.
        var reused = await Json(await client.PostAsJsonAsync($"/api/admin/location-suggestions/{duplicateSuggestion}/approve", new { }), HttpStatusCode.OK);
        Assert.Equal(existingName, reused.GetProperty("resultingEntityId").GetInt32());
        Assert.Equal(districtsBefore + 1, await _factory.InScopeAsync(s => s.GetRequiredService<AppDbContext>().Districts.CountAsync()));
    }

    [Fact(DisplayName = "D05/D06/D07: admin actions on unaudited controllers leave a row; the log is readable by admins only, with masked IPs")]
    public async Task AdminActionsAreAuditedAndReadable()
    {
        var admin = await _factory.SeedUserAsync("admin", Admin);
        var submitter = await _factory.SeedUserAsync("submitter");
        var client = _factory.AuthedClient(admin.AccessToken);

        Guid suggestionId = default;
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var governorateId = await EnsureGovernorateAsync(db);
            var suggestion = LocationSuggestion.Create(LocationSuggestionType.District, governorateId, "Audit District", submitter.Id, null);
            db.Set<LocationSuggestion>().Add(suggestion);
            await db.SaveChangesAsync();
            suggestionId = suggestion.Id;
        });

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/admin/location-suggestions/{suggestionId}/reject", new { notes = "dup" })).StatusCode);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var row = await db.AuditLogs.SingleAsync(a => a.Action == "Admin.LocationSuggestions.Reject");
            Assert.Equal(admin.Id, row.UserId);
            Assert.Contains(suggestionId.ToString(), row.NewValue);
            Assert.DoesNotContain("dup", row.NewValue); // request bodies are never recorded
            // Masked at write time: never a full address (the test host connects from loopback).
            Assert.True(row.IpAddress is null or "127.0.0.0" or "0.0.0.0" or "::", row.IpAddress);
        });

        // Readable by admins, closed to everyone else.
        var page = await Json(await client.GetAsync("/api/admin/audit-logs?action=Admin.LocationSuggestions.Reject"), HttpStatusCode.OK);
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(admin.Id, page.GetProperty("data")[0].GetProperty("userId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.AuthedClient(submitter.AccessToken).GetAsync("/api/admin/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/admin/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/audit-logs?from=2030-01-01&to=2020-01-01")).StatusCode);
    }

    [Fact(DisplayName = "D08: a huge version number on the public update check is a 4xx, not a 500")]
    public async Task UpdateCheckWithOverflowingVersionIsNotA500()
    {
        var response = await _factory.CreateClient().GetAsync("/api/app-updates/check?platform=Android&currentVersion=99999999999.1.1");
        Assert.True((int)response.StatusCode is >= 400 and < 500, $"got {(int)response.StatusCode}");
    }
}
