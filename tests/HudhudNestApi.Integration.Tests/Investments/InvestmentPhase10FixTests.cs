using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Domain.Investments.Enums;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Investments;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Integration.Tests.Investments;

/// <summary>
/// QA Phase 10 fixes (P10-D01..D13), each proven over real HTTP against a real Postgres. Every test describes
/// the behaviour the audit found broken; the "before" behaviour is recorded in
/// qa-plan/phase-10-investments.md and docs/investment/PHASE-10-FIXES.md.
/// </summary>
[Trait("Feature", "Investments")]
[Trait("Category", "QaPhase10")]
public sealed class InvestmentPhase10FixTests : IClassFixture<InvestmentApiTestFactory>, IAsyncLifetime
{
    private const string Admin = "/api/admin/investments/projects";
    private const string Public = "/api/investments";

    private readonly InvestmentApiTestFactory _factory;

    public InvestmentPhase10FixTests(InvestmentApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(SeededUser admin, Guid propertyId, Guid projectId)> SeedAsync(
        InvestmentProjectStatus status, bool withReadiness = true)
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, status);
        if (withReadiness)
        {
            await _factory.SeedFinancialsAsync(projectId);
            await _factory.SeedRiskAsync(projectId);
            await _factory.SeedDocumentAsync(projectId, isPublic: true, InvestmentDocumentType.ProjectPlan);
            await _factory.SeedDocumentAsync(projectId, isPublic: true, InvestmentDocumentType.FinancialStatement);
        }
        return (admin, propertyId, projectId);
    }

    private Task SqlAsync(string sql, params object[] args) => _factory.InScopeAsync(async services =>
        await services.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(sql, args));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    // ── D06: NUL / LIKE wildcards ─────────────────────────────────────

    [Theory(DisplayName = "P10-D06: a NUL character in search or city is a 422, never a 500 (public and admin)")]
    [InlineData("/api/investments/projects?search=%00", false)]
    [InlineData("/api/investments/projects?city=a%00b", false)]
    [InlineData("/api/admin/investments/projects?search=%00", true)]
    public async Task NulInSearch_IsRejectedWith422(string url, bool asAdmin)
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var client = _factory.AuthedClient(asAdmin ? admin.AccessToken : null);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact(DisplayName = "P10-D06: a percent or underscore in the search box is text, not a wildcard")]
    public async Task PercentInSearch_IsNotAWildcard()
    {
        await SeedAsync(InvestmentProjectStatus.Published);
        var client = _factory.AuthedClient();

        var all = await JsonAsync(await client.GetAsync($"{Public}/projects"));
        Assert.True(all.GetProperty("totalCount").GetInt32() >= 1);

        foreach (var term in new[] { "%25", "_", "%25%25" })
        {
            var json = await JsonAsync(await client.GetAsync($"{Public}/projects?search={term}"));
            Assert.Equal(0, json.GetProperty("totalCount").GetInt32());
        }
    }

    // ── D07: calculator caps ──────────────────────────────────────────

    [Theory(DisplayName = "P10-D07: the anonymous calculator rejects absurd amounts and terms with 422 instead of crashing")]
    [InlineData("1e30", 12)]
    [InlineData("79228162514264337593543950335", 12)]
    [InlineData("1000", 100000)]
    public async Task Calculator_RejectsAbsurdInput(string amount, int term)
    {
        var (_, _, projectId) = await SeedAsync(InvestmentProjectStatus.Published);

        var response = await _factory.AuthedClient().PostAsync(
            $"{Public}/projects/{projectId}/calculator",
            new StringContent($"{{\"amount\":{amount},\"termMonths\":{term}}}", System.Text.Encoding.UTF8, "application/json"));

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity || response.StatusCode == HttpStatusCode.BadRequest,
            $"expected 4xx validation, got {(int)response.StatusCode}");
    }

    [Fact(DisplayName = "P10-D07: a sane calculator request still works")]
    public async Task Calculator_SaneInput_Works()
    {
        var (_, _, projectId) = await SeedAsync(InvestmentProjectStatus.Published);

        var response = await _factory.AuthedClient().PostAsJsonAsync(
            $"{Public}/projects/{projectId}/calculator", new { amount = 5000m, termMonths = 12 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(DisplayName = "P10-D07 (rest): the calculator refuses amounts outside the project's own minimum / maximum")]
    [InlineData("999", false)]
    [InlineData("1000", true)]
    [InlineData("50000", true)]
    [InlineData("50001", false)]
    public async Task Calculator_RespectsProjectMinAndMax(string amount, bool accepted)
    {
        var (_, _, projectId) = await SeedAsync(InvestmentProjectStatus.Published);
        await SqlAsync("UPDATE \"InvestmentProjects\" SET \"MaximumInvestment\" = 50000 WHERE \"Id\" = {0}", projectId);

        var response = await _factory.AuthedClient().PostAsync(
            $"{Public}/projects/{projectId}/calculator",
            new StringContent($"{{\"amount\":{amount},\"termMonths\":12}}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(accepted ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── D08: parallel watchlist / interest ────────────────────────────

    [Fact(DisplayName = "P10-D08: parallel watchlist adds never 500; exactly one row is kept")]
    public async Task ParallelWatchlistAdd_NeverReturns500()
    {
        var (_, _, projectId) = await SeedAsync(InvestmentProjectStatus.Published);
        var user = await _factory.SeedUserAsync("watcher");
        var client = _factory.AuthedClient(user.AccessToken);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.PostAsync($"{Public}/projects/{projectId}/watchlist", null)));

        Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"got {(int)r.StatusCode}"));
        Assert.Contains(responses, r => r.IsSuccessStatusCode);
        var rows = await _factory.InScopeAsync(async s =>
            await s.GetRequiredService<AppDbContext>().InvestmentWatchlistItems.CountAsync(w => w.UserId == user.Id));
        Assert.Equal(1, rows);
    }

    [Fact(DisplayName = "P10-D08: parallel interest expressions never 500; exactly one row is kept")]
    public async Task ParallelInterest_NeverReturns500()
    {
        var (_, _, projectId) = await SeedAsync(InvestmentProjectStatus.Published);
        var user = await _factory.SeedUserAsync("investor");
        var client = _factory.AuthedClient(user.AccessToken);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.PostAsync($"{Public}/projects/{projectId}/interest", null)));

        Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"got {(int)r.StatusCode}"));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        var rows = await _factory.InScopeAsync(async s =>
            await s.GetRequiredService<AppDbContext>().InvestmentInterests.CountAsync(i => i.UserId == user.Id));
        Assert.Equal(1, rows);
    }

    // ── D09: admin validation ─────────────────────────────────────────

    private static object ValidUpdate(
        string title = "Valid title", string currency = "USD", decimal min = 1000m, decimal target = 100000m,
        decimal returnMax = 10m, int term = 12) => new
        {
            title,
            shortDescription = "short",
            description = "A valid description.",
            projectType = "Residential",
            targetAmount = target,
            minimumInvestment = min,
            maximumInvestment = (decimal?)null,
            currency,
            investmentTermMonths = term,
            expectedReturnMin = 5m,
            expectedReturnMax = returnMax,
            startDate = (string?)null,
            endDate = (string?)null,
            raisedAmount = 0m,
        };

    [Theory(DisplayName = "P10-D09: weak admin input is refused (currency, minimum > target, return > 100%, term, guaranteed-return title)")]
    [InlineData("currency")]
    [InlineData("minTarget")]
    [InlineData("returnCap")]
    [InlineData("term")]
    [InlineData("title")]
    [InlineData("nul")]
    public async Task AdminUpdate_RejectsWeakInput(string kind)
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Draft, withReadiness: false);
        object body = kind switch
        {
            "currency" => ValidUpdate(currency: "XYZ"),
            "minTarget" => ValidUpdate(min: 200000m, target: 100000m),
            "returnCap" => ValidUpdate(returnMax: 150m),
            "term" => ValidUpdate(term: 5000),
            "title" => ValidUpdate(title: "ربح مؤكد 100%"),
            _ => ValidUpdate(title: "bad\u0000title"),
        };

        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync($"{Admin}/{projectId}", body);

        Assert.True(response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest,
            $"{kind}: got {(int)response.StatusCode}");
        var control = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync($"{Admin}/{projectId}", ValidUpdate());
        Assert.Equal(HttpStatusCode.NoContent, control.StatusCode);
    }

    [Theory(DisplayName = "P10-D09 (rest): an absurd start or end date is refused; a project already under way is still allowed")]
    [InlineData(-36500, null, false)]   // a century ago
    [InlineData(-400, null, true)]      // started last year
    [InlineData(30, null, true)]
    [InlineData(5000, null, false)]     // ~14 years ahead
    [InlineData(30, 40000, false)]      // end date ~110 years ahead
    [InlineData(30, 400, true)]
    public async Task AdminUpdate_StartAndEndDateWindow(int startOffsetDays, int? endOffsetDays, bool accepted)
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Draft, withReadiness: false);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var body = new
        {
            title = "Valid title",
            shortDescription = "short",
            description = "A valid description.",
            projectType = "Residential",
            targetAmount = 100000m,
            minimumInvestment = 1000m,
            maximumInvestment = (decimal?)null,
            currency = "USD",
            investmentTermMonths = 12,
            expectedReturnMin = 5m,
            expectedReturnMax = 10m,
            startDate = today.AddDays(startOffsetDays).ToString("yyyy-MM-dd"),
            endDate = endOffsetDays is { } e ? today.AddDays(e).ToString("yyyy-MM-dd") : null,
            raisedAmount = 0m,
        };

        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync($"{Admin}/{projectId}", body);

        Assert.Equal(accepted ? HttpStatusCode.NoContent : HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact(DisplayName = "P10-D09: an honest guarantee disclaimer in the description is not banned")]
    public async Task HonestDisclaimer_IsAllowed()
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Draft, withReadiness: false);
        var body = new
        {
            title = "Valid title",
            shortDescription = "short",
            description = "العائد غير مضمون وقد تخسر جزءًا من رأس المال. Returns are not guaranteed.",
            projectType = "Residential",
            targetAmount = 100000m,
            minimumInvestment = 1000m,
            maximumInvestment = (decimal?)null,
            currency = "USD",
            investmentTermMonths = 12,
            expectedReturnMin = 5m,
            expectedReturnMax = 10m,
            startDate = (string?)null,
            endDate = (string?)null,
            raisedAmount = 0m,
        };

        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync($"{Admin}/{projectId}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── D10: required documents stay ──────────────────────────────────

    [Fact(DisplayName = "P10-D10: the last public required document of a live project cannot be deleted or hidden")]
    public async Task LastRequiredDocument_CannotBeRemovedOrHidden()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Published);
        var plan = await _factory.SeedDocumentAsync(projectId, isPublic: true, InvestmentDocumentType.ProjectPlan);
        await _factory.SeedDocumentAsync(projectId, isPublic: true, InvestmentDocumentType.FinancialStatement);
        var client = _factory.AuthedClient(admin.AccessToken);

        var hide = await client.PutAsJsonAsync($"{Admin}/{projectId}/documents/{plan}/visibility", new { isPublic = false });
        var delete = await client.DeleteAsync($"{Admin}/{projectId}/documents/{plan}");

        Assert.Equal(HttpStatusCode.BadRequest, hide.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);

        // With a replacement of the same type, removal is fine.
        await _factory.SeedDocumentAsync(projectId, isPublic: true, InvestmentDocumentType.ProjectPlan);
        var deleteAfterReplacement = await client.DeleteAsync($"{Admin}/{projectId}/documents/{plan}");
        Assert.Equal(HttpStatusCode.NoContent, deleteAfterReplacement.StatusCode);
    }

    [Fact(DisplayName = "P10-D10: a draft project's documents stay freely removable")]
    public async Task DraftProject_DocumentsRemovable()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);
        var plan = await _factory.SeedDocumentAsync(projectId, isPublic: true, InvestmentDocumentType.ProjectPlan);

        var delete = await _factory.AuthedClient(admin.AccessToken).DeleteAsync($"{Admin}/{projectId}/documents/{plan}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact(DisplayName = "P10-D10: no update can be added to a closed project")]
    public async Task ClosedProject_RefusesUpdates()
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Closed, withReadiness: false);

        var response = await _factory.AuthedClient(admin.AccessToken).PostAsJsonAsync(
            $"{Admin}/{projectId}/updates", new { title = "x", content = "y", updateType = "Milestone" });

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    // ── D11: docs / updates / risk follow the property ────────────────

    [Fact(DisplayName = "P10-D11: documents, updates and risk of a project disappear with its deleted property")]
    public async Task SubResources_HiddenWhenPropertyDeleted()
    {
        var (admin, propertyId, projectId) = await SeedAsync(InvestmentProjectStatus.Published);
        var adminClient = _factory.AuthedClient(admin.AccessToken);
        var add = await adminClient.PostAsJsonAsync(
            $"{Admin}/{projectId}/updates", new { title = "Progress", content = "Site works started.", updateType = "Milestone" });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var anon = _factory.AuthedClient();

        Assert.Equal(2, (await JsonAsync(await anon.GetAsync($"{Public}/projects/{projectId}/documents"))).GetArrayLength());
        Assert.Equal(1, (await JsonAsync(await anon.GetAsync($"{Public}/projects/{projectId}/updates"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"{Public}/projects/{projectId}/risk")).StatusCode);

        await SqlAsync("UPDATE \"Properties\" SET \"IsDeleted\" = TRUE WHERE \"Id\" = {0}", propertyId);

        Assert.Equal(0, (await JsonAsync(await anon.GetAsync($"{Public}/projects/{projectId}/documents"))).GetArrayLength());
        Assert.Equal(0, (await JsonAsync(await anon.GetAsync($"{Public}/projects/{projectId}/updates"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"{Public}/projects/{projectId}/risk")).StatusCode);
    }

    // ── D04 + D05: resume, and the user lists follow visibility ───────

    [Fact(DisplayName = "P10-D04/D05: a suspended project leaves the user's lists, and resuming brings it back")]
    public async Task SuspendResume_RoundTrip_AndUserLists()
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Published);
        var user = await _factory.SeedUserAsync("investor");
        var adminClient = _factory.AuthedClient(admin.AccessToken);
        var userClient = _factory.AuthedClient(user.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await userClient.PostAsync($"{Public}/projects/{projectId}/watchlist", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await userClient.PostAsync($"{Public}/projects/{projectId}/interest", null)).StatusCode);
        Assert.Equal(1, (await JsonAsync(await userClient.GetAsync($"{Public}/watchlist"))).GetArrayLength());
        Assert.Equal(1, (await JsonAsync(await userClient.GetAsync($"{Public}/interests"))).GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.PostAsync($"{Admin}/{projectId}/suspend", null)).StatusCode);

        Assert.Equal(0, (await JsonAsync(await userClient.GetAsync($"{Public}/watchlist"))).GetArrayLength());
        Assert.Equal(0, (await JsonAsync(await userClient.GetAsync($"{Public}/interests"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.AuthedClient().GetAsync($"{Public}/projects/{projectId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.PostAsync($"{Admin}/{projectId}/resume", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _factory.AuthedClient().GetAsync($"{Public}/projects/{projectId}")).StatusCode);
        Assert.Equal(1, (await JsonAsync(await userClient.GetAsync($"{Public}/watchlist"))).GetArrayLength());
        Assert.Equal(1, (await JsonAsync(await userClient.GetAsync($"{Public}/interests"))).GetArrayLength());
    }

    [Theory(DisplayName = "P10-D04: resume works only from Suspended")]
    [InlineData(InvestmentProjectStatus.Published)]
    [InlineData(InvestmentProjectStatus.Closed)]
    [InlineData(InvestmentProjectStatus.Draft)]
    public async Task Resume_FromOtherStatus_IsRejected(InvestmentProjectStatus from)
    {
        var (admin, _, projectId) = await SeedAsync(from, withReadiness: false);

        var response = await _factory.AuthedClient(admin.AccessToken).PostAsync($"{Admin}/{projectId}/resume", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "P10-D04: only an admin can resume")]
    public async Task Resume_RequiresAdmin()
    {
        var (_, _, projectId) = await SeedAsync(InvestmentProjectStatus.Suspended, withReadiness: false);
        var user = await _factory.SeedUserAsync("investor");

        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.AuthedClient(user.AccessToken).PostAsync($"{Admin}/{projectId}/resume", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.AuthedClient().PostAsync($"{Admin}/{projectId}/resume", null)).StatusCode);
    }

    // ── D12: progress after publish ───────────────────────────────────

    [Fact(DisplayName = "P10-D12: the raised amount of a published project can be corrected, within 0..target")]
    public async Task Progress_CanBeUpdatedAfterPublish()
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Published);
        var client = _factory.AuthedClient(admin.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"{Admin}/{projectId}/progress", new { raisedAmount = 40000m })).StatusCode);
        var details = await JsonAsync(await _factory.AuthedClient().GetAsync($"{Public}/projects/{projectId}"));
        Assert.Equal(40000m, details.GetProperty("raisedAmount").GetDecimal());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"{Admin}/{projectId}/progress", new { raisedAmount = 100001m })).StatusCode);
        Assert.True((await client.PutAsJsonAsync($"{Admin}/{projectId}/progress", new { raisedAmount = -1m })).StatusCode
            is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    [Theory(DisplayName = "P10-D12: progress is refused on a draft or closed project")]
    [InlineData(InvestmentProjectStatus.Draft)]
    [InlineData(InvestmentProjectStatus.Closed)]
    public async Task Progress_RefusedOutsideLiveStates(InvestmentProjectStatus from)
    {
        var (admin, _, projectId) = await SeedAsync(from, withReadiness: false);

        var response = await _factory.AuthedClient(admin.AccessToken)
            .PutAsJsonAsync($"{Admin}/{projectId}/progress", new { raisedAmount = 10m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── D03: scheduled publication ────────────────────────────────────

    private async Task<int> SweepAsync()
    {
        var service = new InvestmentScheduledPublishHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InvestmentScheduledPublishHostedService>.Instance);
        return await service.RunOnceAsync(CancellationToken.None);
    }

    private async Task<string> StatusOfAsync(Guid projectId) => await _factory.InScopeAsync(async s =>
        (await s.GetRequiredService<AppDbContext>().InvestmentProjects.AsNoTracking().SingleAsync(p => p.Id == projectId)).Status.ToString());

    [Fact(DisplayName = "P10-D03: a project scheduled for later cannot be published by hand, and goes live by itself when due")]
    public async Task ScheduledProject_IsPublishedByTheSweep_NotByHandBeforeTime()
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Approved);
        var client = _factory.AuthedClient(admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(
            $"{Admin}/{projectId}/schedule", new { scheduledPublishAt = DateTime.UtcNow.AddHours(2) })).StatusCode);

        // Before the date: manual publish refused, the sweep leaves it alone.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"{Admin}/{projectId}/publish", null)).StatusCode);
        Assert.Equal(0, await SweepAsync());
        Assert.Equal("Scheduled", await StatusOfAsync(projectId));

        // The date passes.
        await SqlAsync("UPDATE \"InvestmentProjects\" SET \"ScheduledPublishAt\" = {0} WHERE \"Id\" = {1}",
            DateTime.UtcNow.AddMinutes(-1), projectId);

        Assert.Equal(1, await SweepAsync());
        Assert.Equal("Published", await StatusOfAsync(projectId));
        Assert.Equal(HttpStatusCode.OK, (await _factory.AuthedClient().GetAsync($"{Public}/projects/{projectId}")).StatusCode);
        Assert.Equal(0, await SweepAsync());
    }

    [Fact(DisplayName = "P10-D03: a due project that is no longer ready stays scheduled; the sweep does not fail")]
    public async Task DueButNotReady_StaysScheduled()
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Approved, withReadiness: false);
        var client = _factory.AuthedClient(admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(
            $"{Admin}/{projectId}/schedule", new { scheduledPublishAt = DateTime.UtcNow.AddHours(1) })).StatusCode);
        await SqlAsync("UPDATE \"InvestmentProjects\" SET \"ScheduledPublishAt\" = {0} WHERE \"Id\" = {1}",
            DateTime.UtcNow.AddMinutes(-1), projectId);

        Assert.Equal(0, await SweepAsync());
        Assert.Equal("Scheduled", await StatusOfAsync(projectId));

        // Once the gaps are filled, the next sweep publishes it.
        await _factory.SeedFinancialsAsync(projectId);
        await _factory.SeedRiskAsync(projectId);
        await _factory.SeedDocumentAsync(projectId, isPublic: true, InvestmentDocumentType.ProjectPlan);
        await _factory.SeedDocumentAsync(projectId, isPublic: true, InvestmentDocumentType.FinancialStatement);
        Assert.Equal(1, await SweepAsync());
        Assert.Equal("Published", await StatusOfAsync(projectId));
    }

    [Fact(DisplayName = "P10-D03: a project scheduled without a date is never touched by the sweep and stays manual")]
    public async Task ScheduledWithoutDate_StaysManual()
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Scheduled);

        Assert.Equal(0, await SweepAsync());
        Assert.Equal("Scheduled", await StatusOfAsync(projectId));
        Assert.Equal(HttpStatusCode.NoContent,
            (await _factory.AuthedClient(admin.AccessToken).PostAsync($"{Admin}/{projectId}/publish", null)).StatusCode);
    }

    // ── D01: interest reaches the admins ──────────────────────────────

    [Fact(DisplayName = "P10-D01: an expression of interest is visible to admins (list, count) and notifies them")]
    public async Task Interest_ReachesAdmins()
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Published);
        var investor = await _factory.SeedUserAsync("investor");
        var adminClient = _factory.AuthedClient(admin.AccessToken);

        Assert.Equal(0, (await JsonAsync(await adminClient.GetAsync($"{Admin}/{projectId}/interests"))).GetArrayLength());

        Assert.Equal(HttpStatusCode.Created,
            (await _factory.AuthedClient(investor.AccessToken).PostAsync($"{Public}/projects/{projectId}/interest", null)).StatusCode);

        var list = await JsonAsync(await adminClient.GetAsync($"{Admin}/{projectId}/interests"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(investor.Id, list[0].GetProperty("userId").GetGuid());
        Assert.Equal(investor.Email, list[0].GetProperty("email").GetString());
        Assert.Equal("Active", list[0].GetProperty("status").GetString());

        var projects = await JsonAsync(await adminClient.GetAsync(Admin));
        var row = projects.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == projectId);
        Assert.Equal(1, row.GetProperty("activeInterestCount").GetInt32());

        await _factory.InScopeAsync(async s =>
        {
            var note = await s.GetRequiredService<AppDbContext>().Notifications
                .SingleAsync(n => n.RecipientId == admin.Id && n.Type == NotificationType.InvestmentInterestReceived);
            Assert.Equal(projectId, note.RelatedEntityId);
            Assert.DoesNotContain(investor.Email, note.Message);
        });

        // Withdrawn interests stay on the list (status shown) but leave the active count.
        Assert.Equal(HttpStatusCode.NoContent,
            (await _factory.AuthedClient(investor.AccessToken).DeleteAsync($"{Public}/projects/{projectId}/interest")).StatusCode);
        var afterWithdraw = await JsonAsync(await adminClient.GetAsync($"{Admin}/{projectId}/interests"));
        Assert.Equal("Withdrawn", afterWithdraw[0].GetProperty("status").GetString());
        var projectsAfter = await JsonAsync(await adminClient.GetAsync(Admin));
        Assert.Equal(0, projectsAfter.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == projectId).GetProperty("activeInterestCount").GetInt32());
    }

    [Fact(DisplayName = "P10-D01: repeating the same interest does not notify the admins twice")]
    public async Task RepeatedInterest_NotifiesOnce()
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Published);
        var investor = await _factory.SeedUserAsync("investor");
        var client = _factory.AuthedClient(investor.AccessToken);

        await client.PostAsync($"{Public}/projects/{projectId}/interest", null);
        var second = await client.PostAsync($"{Public}/projects/{projectId}/interest", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var count = await _factory.InScopeAsync(async s => await s.GetRequiredService<AppDbContext>().Notifications
            .CountAsync(n => n.RecipientId == admin.Id && n.Type == NotificationType.InvestmentInterestReceived));
        Assert.Equal(1, count);
    }

    [Fact(DisplayName = "P10-D01: the interest list is admin-only and 404s for an unknown project")]
    public async Task InterestList_IsAdminOnly()
    {
        var (admin, _, projectId) = await SeedAsync(InvestmentProjectStatus.Published);
        var user = await _factory.SeedUserAsync("investor");

        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.AuthedClient(user.AccessToken).GetAsync($"{Admin}/{projectId}/interests")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.AuthedClient().GetAsync($"{Admin}/{projectId}/interests")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.AuthedClient(admin.AccessToken).GetAsync($"{Admin}/{Guid.NewGuid()}/interests")).StatusCode);
    }
}
