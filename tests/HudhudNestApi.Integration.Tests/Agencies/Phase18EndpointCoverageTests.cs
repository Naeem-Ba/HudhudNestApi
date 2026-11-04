using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Agencies;

/// <summary>
/// QA Phase 18, P18-D05: HTTP coverage for endpoints that had no caller in the app and no test, plus the HTTP
/// side of the D01 / D02 / D09 fixes. Each row pins the status an anonymous caller, an ordinary user and an
/// administrator get for an id that does not exist - the three things that matter on an admin surface: the
/// gate (401 / 403), the not-found handling (404 rather than 500) and that nothing answers 5xx.
/// </summary>
public sealed class Phase18EndpointCoverageTests : IAsyncLifetime
{
    private static readonly Guid Unknown = Guid.Parse("00000000-0000-4000-8000-000000000018");

    private readonly InvestmentApiTestFactory _factory = new();
    private SeededUser _admin = null!;
    private SeededUser _user = null!;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        _admin = await _factory.SeedUserAsync("p18-admin", RoleNames.Admin);
        _user = await _factory.SeedUserAsync("p18-user");
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Client(string? token) => token is null ? _factory.CreateClient() : _factory.AuthedClient(token);

    private static readonly object Reason = new { Reason = "سبب الاختبار" };
    private static readonly object Note = new { Note = "ملاحظة الاختبار" };

    /// <summary>method, path, body, expected anonymous, expected ordinary user, expected administrator.</summary>
    public static IEnumerable<object?[]> AdminSurface()
    {
        var id = Unknown;
        object?[] Row(string m, string p, object? body, int anon, int user, int admin) => [m, p, body, anon, user, admin];

        // Valuation office flags
        yield return Row("POST", $"/api/Admin/valuation-offices/{id}/flag", Reason, 401, 403, 404);
        yield return Row("POST", $"/api/Admin/valuation-offices/{id}/unflag", new { }, 401, 403, 404);

        // Listing-report admin actions
        yield return Row("POST", $"/api/admin/listing-reports/{id}/start-review", new { }, 401, 403, 404);
        yield return Row("POST", $"/api/admin/listing-reports/{id}/assign", new { AdminUserId = (Guid?)null }, 401, 403, 404);
        yield return Row("POST", $"/api/admin/listing-reports/{id}/priority", new { Priority = 1 }, 401, 403, 404);
        yield return Row("POST", $"/api/admin/listing-reports/{id}/notes", Note, 401, 403, 404);
        yield return Row("POST", $"/api/admin/listing-reports/{id}/request-info", new { Message = "رسالة" }, 401, 403, 404);
        yield return Row("POST", $"/api/admin/listing-reports/{id}/action-required", Note, 401, 403, 404);
        yield return Row("POST", $"/api/admin/listing-reports/{id}/reopen", Reason, 401, 403, 404);
        yield return Row("POST", $"/api/admin/listing-reports/{id}/restore-listing", Reason, 401, 403, 404);

        // Admin-only property payment confirmation and statistics
        yield return Row("POST", $"/api/Properties/extension/{id}/confirm", new { }, 401, 403, 404);
        yield return Row("POST", $"/api/Properties/featured/{id}/confirm", new { }, 401, 403, 404);
        yield return Row("GET", "/api/Properties/social-performance", null, 401, 403, 200);
        yield return Row("GET", "/api/operational/version", null, 401, 403, 200);

        // Social distribution (admin only)
        yield return Row("GET", "/api/social-distribution/publishers/Facebook/capabilities", null, 401, 403, 200);
        yield return Row("GET", $"/api/social-distribution/rules/{id}", null, 401, 403, 404);
        yield return Row("POST", "/api/social-distribution/rules/validate", new { }, 401, 403, 400);
        yield return Row("POST", $"/api/social-distribution/evaluate/{id}", new { }, 401, 403, 409);
        yield return Row("POST", $"/api/social-distribution/dispatch/{id}", new { }, 401, 403, 409);
        yield return Row("GET", $"/api/social-distribution/publications/{id}/history", null, 401, 403, 200);
        yield return Row("POST", $"/api/social-distribution/publications/{id}/reschedule", new { NewScheduledAt = DateTime.UtcNow.AddDays(1) }, 401, 403, 404);
        yield return Row("POST", $"/api/social-distribution/publications/{id}/retry", new { }, 401, 403, 404);
        yield return Row("POST", $"/api/social-distribution/publications/{id}/repost", new { }, 401, 403, 404);
        yield return Row("POST", $"/api/social-distribution/publications/{id}/cancel", new { }, 401, 403, 404);
        yield return Row("POST", $"/api/social-distribution/publications/{id}/content/approve", new { }, 401, 403, 404);
        yield return Row("POST", $"/api/social-distribution/publications/{id}/content/reject", new { }, 401, 403, 400);
        yield return Row("GET", $"/api/social-distribution/dead-letters/{id}", null, 401, 403, 404);
        yield return Row("GET", $"/api/social-distribution/assets/{id}", null, 401, 403, 404);
        yield return Row("POST", $"/api/social-distribution/assets/{id}/regenerate", new { }, 401, 403, 404);
        // P18-D02: an unknown property used to be a 500.
        yield return Row("POST", "/api/social-distribution/assets/generate",
            new { PropertyId = id, Platform = 1, Language = "ar", Title = "عنوان", Body = "نص" }, 401, 403, 404);
    }

    [Theory]
    [MemberData(nameof(AdminSurface))]
    public async Task AdminEndpoints_GateTheCaller_AndAnswerNotFoundNotServerError(
        string method, string path, object? body, int anonymous, int user, int admin)
    {
        foreach (var (token, expected, who) in new[]
                 {
                     ((string?)null, anonymous, "anonymous"),
                     (_user.AccessToken, user, "user"),
                     (_admin.AccessToken, admin, "admin"),
                 })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method == "POST")
                request.Content = JsonContent.Create(body ?? new { });

            var response = await Client(token).SendAsync(request);

            Assert.True((int)response.StatusCode == expected,
                $"{method} {path} as {who}: expected {expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    // ── Hidden operational endpoints ─────────────────────────────

    [Theory]
    [InlineData("/internal/observability/synthetic")]
    [InlineData("/internal/observability/synthetic/alert-test-state")]
    public async Task InternalObservabilityEndpoints_PretendNotToExist_WithoutTheSecret(string path)
    {
        foreach (var token in new[] { null, _user.AccessToken, _admin.AccessToken })
        {
            var response = await Client(token).PostAsJsonAsync(path, new { });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task CspReport_AcceptsValidAndGarbageBodies_WithNoContent_AndRefusesOversizedOnes()
    {
        var client = Client(null);

        var legacy = new StringContent(
            "{\"csp-report\":{\"document-uri\":\"https://hudhudnest.com/\",\"blocked-uri\":\"inline\",\"effective-directive\":\"script-src\"}}",
            System.Text.Encoding.UTF8, "application/csp-report");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/security/csp-report", legacy)).StatusCode);

        var garbage = new StringContent("not json at all", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/security/csp-report", garbage)).StatusCode);

        var huge = new StringContent(new string('x', 20_000), System.Text.Encoding.UTF8, "application/json");
        var tooBig = await client.PostAsync("/api/security/csp-report", huge);
        Assert.True((int)tooBig.StatusCode is 413 or 400, $"got {(int)tooBig.StatusCode}");
    }

    // ── Owner-facing endpoints ───────────────────────────────────

    [Fact]
    public async Task PropertiesMine_ListsOnlyTheCallersOwnListings_AndNeedsSignIn()
    {
        var other = await _factory.SeedUserAsync("p18-other");
        var mine = await _factory.SeedPropertyAsync(_user.Id);
        var theirs = await _factory.SeedPropertyAsync(other.Id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/api/Properties/mine")).StatusCode);

        var response = await Client(_user.AccessToken).GetAsync("/api/Properties/mine");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(mine.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(theirs.ToString(), text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchRequestRenew_RequiresSignIn_AndAnUnknownRequestIsNotFound()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).PostAsJsonAsync($"/api/search-requests/{Unknown}/renew", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(_user.AccessToken).PostAsJsonAsync($"/api/search-requests/{Unknown}/renew", new { })).StatusCode);
    }

    [Fact]
    public async Task ServiceProviderReviews_ArePublic_AndAnUnknownProviderGivesAnEmptyList()
    {
        // Pinned as observed: public, no token needed, and an id nobody owns is an empty list (not a 500).
        var response = await Client(null).GetAsync($"/api/ServiceProviders/{Unknown}/reviews");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.ValueKind);
    }

    // ── D01 / D09 over HTTP ──────────────────────────────────────

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ProposeListing_WithAnEmptyPropertyId_Answers422_NotA500(string emptyId)
    {
        var response = await Client(_user.AccessToken).PostAsJsonAsync(
            $"/api/search-requests/{Unknown}/proposals", new { PropertyId = Guid.Parse(emptyId) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ProposeListing_WithAMissingBody_NeverAnswers500()
    {
        var response = await Client(_user.AccessToken).PostAsJsonAsync($"/api/search-requests/{Unknown}/proposals", new { });

        Assert.True((int)response.StatusCode < 500, $"got {(int)response.StatusCode}");
    }

    [Theory]
    [InlineData("/api/analytics/market?countryCode=%00")]
    [InlineData("/api/analytics/market?countryCode=S%00Y")]
    [InlineData("/api/analytics/market?countryCode=%07")]
    [InlineData("/api/lookups/governorates?countryCode=%00")]
    [InlineData("/api/lookups/governorates?countryCode=S%0AY")]
    public async Task ControlCharactersInPublicQueryParameters_AreRejectedBeforeTheDatabase(string path)
    {
        var response = await Client(null).GetAsync(path);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/analytics/market")]
    [InlineData("/api/analytics/market?countryCode=SY")]
    [InlineData("/api/lookups/governorates")]
    [InlineData("/api/lookups/governorates?countryCode=SY")]
    public async Task NormalPublicQueryParameters_StillWork(string path)
    {
        var response = await Client(null).GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
