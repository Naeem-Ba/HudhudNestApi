using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HudhudNestApi.Application.Moderation;
using HudhudNestApi.Application.Notifications.DTOs;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Moderation.Enums;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Moderation;

/// <summary>
/// Listing reports over real HTTP, real Identity tokens and real Postgres: report → admin queue →
/// review → info request → answer → listing take-down → close → reporter sees only the general
/// result → reopen. Plus the rules around it: validation, duplicates, replays, daily limit,
/// authorization (no IDOR, no admin access for users), privacy of the reporter and of internal
/// notes, deleted listings, and a failing notification never losing a report.
/// </summary>
public sealed class ListingReportTests : IAsyncLifetime
{
    private const string Admin = "Admin";
    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Task<HttpResponseMessage> Report(HttpClient client, Guid propertyId, string reason = "Fraud",
        string? details = "The owner asked me to transfer a deposit before any viewing.", Guid? clientRequestId = null) =>
        client.PostAsJsonAsync("/api/listing-reports", new { propertyId, reason, details, clientRequestId });

    private Task<HttpResponseMessage> AdminPost(SeededUser admin, Guid reportId, string action, object body) =>
        _factory.AuthedClient(admin.AccessToken).PostAsJsonAsync($"/api/admin/listing-reports/{reportId}/{action}", body);

    private async Task<(SeededUser Owner, SeededUser Reporter, SeededUser Admin, Guid PropertyId)> SeedAsync()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var reporter = await _factory.SeedUserAsync("reporter");
        var admin = await _factory.SeedUserAsync("admin", Admin);
        var propertyId = await _factory.SeedPropertyAsync(owner.Id);
        return (owner, reporter, admin, propertyId);
    }

    [Fact(DisplayName = "Full lifecycle: report, queue, review, info request, answer, take down, close, reporter sees result, reopen")]
    public async Task FullLifecycle()
    {
        var (owner, reporter, admin, propertyId) = await SeedAsync();
        var reporterClient = _factory.AuthedClient(reporter.AccessToken);
        var adminClient = _factory.AuthedClient(admin.AccessToken);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var account = await db.UserAccounts.SingleAsync(u => u.Id == reporter.Id);
            account.ChooseInitialLanguage("de", DateTime.UtcNow);
            await db.SaveChangesAsync();
        });

        // 1. Reasons are served as codes.
        var reasons = await ReadJson(await reporterClient.GetAsync("/api/listing-reports/reasons"), HttpStatusCode.OK);
        Assert.Equal(14, reasons.GetArrayLength());
        Assert.Contains(reasons.EnumerateArray(), r => r.GetProperty("code").GetString() == "Other" && r.GetProperty("requiresDetails").GetBoolean());

        // 2. Report. The body tries to set server-owned fields; they are ignored.
        var created = await ReadJson(await reporterClient.PostAsJsonAsync("/api/listing-reports", new
        {
            propertyId,
            reason = "Fraud",
            details = "<script>alert(1)</script> asked for money upfront",
            status = "Resolved",
            priority = "Low",
            reporterUserId = owner.Id,
        }), HttpStatusCode.Created);
        var reportId = created.GetProperty("id").GetGuid();
        Assert.Matches(@"^RPT-\d{6}$", created.GetProperty("referenceNumber").GetString()!);
        Assert.Equal("Submitted", created.GetProperty("status").GetString());
        Assert.False(created.TryGetProperty("priority", out _));
        Assert.Equal("<script>alert(1)</script> asked for money upfront", created.GetProperty("details").GetString()); // stored as text, rendered as text

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var row = await db.ListingReports.SingleAsync(r => r.Id == reportId);
            Assert.Equal(reporter.Id, row.ReporterUserId);
            Assert.Equal(owner.Id, row.ListingOwnerId);
            Assert.Equal(ListingReportStatus.New, row.Status);
            Assert.Equal(ListingReportPriority.High, row.Priority); // Fraud starts High

            // Admins are told; the message names the listing and reason, not the reporter.
            var adminNote = await db.Notifications.SingleAsync(n => n.RecipientId == admin.Id && n.Type == NotificationType.ListingReportSubmitted);
            Assert.Equal(reportId, adminNote.RelatedEntityId);
            Assert.DoesNotContain(reporter.Email, adminNote.Message);
            // The owner is never told about a report.
            Assert.Equal(0, await db.Notifications.CountAsync(n => n.RecipientId == owner.Id));
        });

        // 3. Admin queue and stats.
        var queue = await ReadJson(await adminClient.GetAsync("/api/admin/listing-reports?status=New&reason=Fraud"), HttpStatusCode.OK);
        Assert.Equal(1, queue.GetProperty("total").GetInt32());
        var row0 = queue.GetProperty("items")[0];
        Assert.Equal(propertyId, row0.GetProperty("propertyId").GetGuid());
        Assert.Equal("ForSale", row0.GetProperty("listingType").GetString());
        var stats = await ReadJson(await adminClient.GetAsync("/api/admin/listing-reports/stats"), HttpStatusCode.OK);
        Assert.Equal(1, stats.GetProperty("total").GetInt32());
        Assert.Equal(1, stats.GetProperty("new").GetInt32());
        Assert.Equal(1, stats.GetProperty("highPriorityOpen").GetInt32());

        // 4. Start review: assigned to the reviewer.
        var reviewing = await ReadJson(await AdminPost(admin, reportId, "start-review", new { }), HttpStatusCode.OK);
        Assert.Equal("UnderReview", reviewing.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal(admin.Id, reviewing.GetProperty("summary").GetProperty("assignedAdminId").GetGuid());

        // 5. Internal note, then a question for the reporter.
        await ReadJson(await AdminPost(admin, reportId, "notes", new { note = "INTERNAL: owner has two other flagged listings" }), HttpStatusCode.OK);
        var asked = await ReadJson(await AdminPost(admin, reportId, "request-info", new { message = "Did you pay anything? Please give the date." }), HttpStatusCode.OK);
        Assert.Equal("WaitingForInformation", asked.GetProperty("summary").GetProperty("status").GetString());

        var mine = await ReadJson(await reporterClient.GetAsync($"/api/listing-reports/me/{reportId}"), HttpStatusCode.OK);
        Assert.Equal("NeedsYourInfo", mine.GetProperty("status").GetString());
        Assert.Equal("Did you pay anything? Please give the date.", mine.GetProperty("infoRequest").GetString());
        var mineText = mine.GetRawText();
        Assert.DoesNotContain("INTERNAL", mineText);
        Assert.DoesNotContain(admin.Id.ToString(), mineText);
        Assert.DoesNotContain(owner.Id.ToString(), mineText);

        // 6. The reporter answers; back to review.
        var answered = await ReadJson(await reporterClient.PostAsJsonAsync(
            $"/api/listing-reports/me/{reportId}/respond", new { message = "No payment yet, he asked on 2026-10-20." }), HttpStatusCode.OK);
        Assert.Equal("InReview", answered.GetProperty("status").GetString());
        Assert.Equal(2, answered.GetProperty("timeline").GetArrayLength());

        // 7. "Taken down" cannot be claimed while the listing is still live.
        var tooEarly = await ReadJson(await AdminPost(admin, reportId, "close", new { outcome = "ListingTakenDown", note = "Confirmed scam." }), HttpStatusCode.Conflict);
        Assert.Equal(ListingReportErrorCodes.ListingNotTakenDown, tooEarly.GetProperty("code").GetString());

        var takenDown = await ReadJson(await AdminPost(admin, reportId, "take-down-listing", new { reason = "Asked for payment before a viewing." }), HttpStatusCode.OK);
        Assert.True(takenDown.GetProperty("listing").GetProperty("isTakenDown").GetBoolean());

        var closed = await ReadJson(await AdminPost(admin, reportId, "close", new { outcome = "ListingTakenDown", note = "Confirmed scam pattern." }), HttpStatusCode.OK);
        Assert.Equal("Resolved", closed.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal("ListingTakenDown", closed.GetProperty("outcome").GetString());

        // 8. Reporter sees the general result only.
        mine = await ReadJson(await reporterClient.GetAsync($"/api/listing-reports/me/{reportId}"), HttpStatusCode.OK);
        Assert.Equal("Closed", mine.GetProperty("status").GetString());
        Assert.Equal("ActionTaken", mine.GetProperty("outcome").GetString());
        mineText = mine.GetRawText();
        Assert.DoesNotContain("Confirmed scam", mineText);
        Assert.DoesNotContain("resolutionNote", mineText);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = await db.Properties.IgnoreQueryFilters().SingleAsync(p => p.Id == propertyId);
            Assert.True(property.IsTakenDown);
            Assert.False(property.IsDeleted); // a report never deletes a listing

            // Reporter notifications, rendered in the reporter's language (de).
            var info = await db.Notifications.SingleAsync(n => n.RecipientId == reporter.Id && n.Type == NotificationType.ListingReportInfoRequested);
            Assert.Contains("weitere Angaben", info.Message);
            var done = await db.Notifications.SingleAsync(n => n.RecipientId == reporter.Id && n.Type == NotificationType.ListingReportClosed);
            Assert.Contains("Maßnahmen", done.Message);
            Assert.DoesNotContain("Confirmed scam", done.Message);

            // The owner learns about the take-down (with the admin's reason), not about the report or reporter.
            var ownerNotes = await db.Notifications.Where(n => n.RecipientId == owner.Id).ToListAsync();
            Assert.Single(ownerNotes);
            Assert.Equal(NotificationType.ListingTakenDown, ownerNotes[0].Type);
            Assert.DoesNotContain(reporter.Email, ownerNotes[0].Message);

            // Audit rows for each decision, attributed to the admin from the token.
            foreach (var action in new[]
            {
                AuditActions.ListingReportReviewStartedByAdmin, AuditActions.ListingReportInfoRequestedByAdmin,
                AuditActions.ListingReportClosedByAdmin, AuditActions.ListingTakenDownByAdmin,
            })
                Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == action && a.UserId == admin.Id));

            var types = await db.ListingReportEvents.Where(e => e.ReportId == reportId).OrderBy(e => e.CreatedAt).Select(e => e.Type).ToListAsync();
            Assert.Equal(
                [
                    ListingReportEventType.Created, ListingReportEventType.StatusChanged, ListingReportEventType.NoteAdded,
                    ListingReportEventType.InfoRequested, ListingReportEventType.ReporterResponded,
                    ListingReportEventType.ListingTakenDown, ListingReportEventType.Closed,
                ],
                types);
        });

        // 9. The admin detail keeps everything the investigation needs.
        var detail = await ReadJson(await adminClient.GetAsync($"/api/admin/listing-reports/{reportId}"), HttpStatusCode.OK);
        Assert.Equal("Confirmed scam pattern.", detail.GetProperty("resolutionNote").GetString());
        Assert.Equal(reporter.Id, detail.GetProperty("reporter").GetProperty("id").GetGuid());
        Assert.Contains(detail.GetProperty("timeline").EnumerateArray(), e => e.GetProperty("note").GetString() == "INTERNAL: owner has two other flagged listings" && e.GetProperty("isInternal").GetBoolean());

        // 10. Reopen, then a second decision; a retry of the close is a no-op 409 and no extra notice.
        var reopened = await ReadJson(await AdminPost(admin, reportId, "reopen", new { reason = "Owner appealed with documents." }), HttpStatusCode.OK);
        Assert.Equal("UnderReview", reopened.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal(1, reopened.GetProperty("reopenCount").GetInt32());
        await ReadJson(await AdminPost(admin, reportId, "restore-listing", new { reason = "Documents checked." }), HttpStatusCode.OK);
        var dismissed = await ReadJson(await AdminPost(admin, reportId, "close", new { outcome = "InsufficientEvidence", note = "Documents are genuine." }), HttpStatusCode.OK);
        Assert.Equal("Dismissed", dismissed.GetProperty("summary").GetProperty("status").GetString());
        await ReadJson(await AdminPost(admin, reportId, "close", new { outcome = "NoViolation", note = "again" }), HttpStatusCode.Conflict);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await db.Notifications.CountAsync(n => n.RecipientId == reporter.Id && n.Type == NotificationType.ListingReportClosed));
        });
    }

    [Fact(DisplayName = "Validation: unknown listing 404, bad reason 422, Other needs details, too long 422, own listing 409, anonymous 401")]
    public async Task Validation()
    {
        var (owner, reporter, _, propertyId) = await SeedAsync();
        var client = _factory.AuthedClient(reporter.AccessToken);

        var missing = await ReadJson(await Report(client, Guid.NewGuid()), HttpStatusCode.NotFound);
        Assert.DoesNotContain("Exception", missing.GetRawText());

        await ReadJson(await client.PostAsJsonAsync("/api/listing-reports", new { propertyId, reason = 999 }), HttpStatusCode.UnprocessableEntity);
        await ReadJson(await Report(client, propertyId, "Other", details: "short"), HttpStatusCode.UnprocessableEntity);
        await ReadJson(await Report(client, propertyId, "Other", details: null), HttpStatusCode.UnprocessableEntity);
        await ReadJson(await Report(client, propertyId, "Fraud", details: new string('x', 2001)), HttpStatusCode.UnprocessableEntity);
        await ReadJson(await Report(client, Guid.Empty), HttpStatusCode.UnprocessableEntity);

        var own = await ReadJson(await Report(_factory.AuthedClient(owner.AccessToken), propertyId), HttpStatusCode.Conflict);
        Assert.Equal(ListingReportErrorCodes.OwnListing, own.GetProperty("code").GetString());

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Report(anonymous, propertyId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/listing-reports/reasons")).StatusCode);

        await _factory.InScopeAsync(async services =>
            Assert.Equal(0, await services.GetRequiredService<AppDbContext>().ListingReports.CountAsync()));
    }

    [Fact(DisplayName = "Duplicates: one open report per listing, replays return the first, same reason waits, a new reason is accepted")]
    public async Task Duplicates()
    {
        var (_, reporter, admin, propertyId) = await SeedAsync();
        var client = _factory.AuthedClient(reporter.AccessToken);
        var dialog = Guid.NewGuid();

        var first = await ReadJson(await Report(client, propertyId, clientRequestId: dialog), HttpStatusCode.Created);
        var replay = await ReadJson(await Report(client, propertyId, clientRequestId: dialog), HttpStatusCode.OK);
        Assert.Equal(first.GetProperty("id").GetGuid(), replay.GetProperty("id").GetGuid());

        var again = await ReadJson(await Report(client, propertyId, "FakePhotos"), HttpStatusCode.Conflict);
        Assert.Equal(ListingReportErrorCodes.AlreadyOpen, again.GetProperty("code").GetString());

        var open = await ReadJson(await client.GetAsync($"/api/listing-reports/me/open?propertyId={propertyId}"), HttpStatusCode.OK);
        Assert.True(open.GetProperty("hasOpenReport").GetBoolean());

        await ReadJson(await AdminPost(admin, first.GetProperty("id").GetGuid(), "close", new { outcome = "NoViolation", note = "Price is normal for the area." }), HttpStatusCode.OK);

        var sameReason = await ReadJson(await Report(client, propertyId, "Fraud"), HttpStatusCode.Conflict);
        Assert.Equal(ListingReportErrorCodes.RecentlyReviewed, sameReason.GetProperty("code").GetString());
        await ReadJson(await Report(client, propertyId, "FakePhotos", details: "Photos are from another building."), HttpStatusCode.Created);

        await _factory.InScopeAsync(async services =>
            Assert.Equal(2, await services.GetRequiredService<AppDbContext>().ListingReports.CountAsync()));
    }

    [Fact(DisplayName = "Daily limit per account answers 429 with a code")]
    public async Task DailyLimit()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var reporter = await _factory.SeedUserAsync("reporter");
        var properties = new List<Guid>();
        for (var i = 0; i < 3; i++)
            properties.Add(await _factory.SeedPropertyAsync(owner.Id));

        using var limited = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.Configure<ListingReportOptions>(o => o.DailyLimitPerReporter = 2)));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", reporter.AccessToken);

        await ReadJson(await Report(client, properties[0]), HttpStatusCode.Created);
        await ReadJson(await Report(client, properties[1]), HttpStatusCode.Created);
        var third = await ReadJson(await Report(client, properties[2]), HttpStatusCode.TooManyRequests);
        Assert.Equal(ListingReportErrorCodes.DailyLimit, third.GetProperty("code").GetString());
    }

    [Fact(DisplayName = "Authorization: users get 403 on admin routes, 404 on someone else's report; assignee must be an admin")]
    public async Task Authorization()
    {
        var (owner, reporter, admin, propertyId) = await SeedAsync();
        var other = await _factory.SeedUserAsync("other");
        var created = await ReadJson(await Report(_factory.AuthedClient(reporter.AccessToken), propertyId), HttpStatusCode.Created);
        var reportId = created.GetProperty("id").GetGuid();

        foreach (var user in new[] { reporter, owner, other })
        {
            var client = _factory.AuthedClient(user.AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/listing-reports")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/admin/listing-reports/{reportId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/listing-reports/{reportId}/close", new { outcome = "NoViolation", note = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/listing-reports/{reportId}/take-down-listing", new { reason = "x" })).StatusCode);
        }

        // Owner and strangers cannot read or answer the reporter's report.
        foreach (var user in new[] { owner, other })
        {
            var client = _factory.AuthedClient(user.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/listing-reports/me/{reportId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/listing-reports/me/{reportId}/respond", new { message = "x" })).StatusCode);
            var list = await ReadJson(await client.GetAsync("/api/listing-reports/me"), HttpStatusCode.OK);
            Assert.Equal(0, list.GetProperty("total").GetInt32());
        }

        var notAdmin = await ReadJson(await AdminPost(admin, reportId, "assign", new { adminUserId = other.Id }), HttpStatusCode.Conflict);
        Assert.Equal(ListingReportErrorCodes.InvalidAssignee, notAdmin.GetProperty("code").GetString());
        var otherAdmin = await _factory.SeedUserAsync("admin2", Admin);
        var assigned = await ReadJson(await AdminPost(admin, reportId, "assign", new { adminUserId = otherAdmin.Id }), HttpStatusCode.OK);
        Assert.Equal(otherAdmin.Id, assigned.GetProperty("summary").GetProperty("assignedAdminId").GetGuid());
        var assignees = await ReadJson(await _factory.AuthedClient(admin.AccessToken).GetAsync("/api/admin/listing-reports/assignees"), HttpStatusCode.OK);
        Assert.Equal(2, assignees.GetArrayLength());
    }

    [Fact(DisplayName = "Transitions are enforced on the server")]
    public async Task Transitions()
    {
        var (_, reporter, admin, propertyId) = await SeedAsync();
        var reporterClient = _factory.AuthedClient(reporter.AccessToken);
        var reportId = (await ReadJson(await Report(reporterClient, propertyId), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        await ReadJson(await AdminPost(admin, reportId, "reopen", new { reason = "x" }), HttpStatusCode.Conflict);           // not closed
        await ReadJson(await reporterClient.PostAsJsonAsync($"/api/listing-reports/me/{reportId}/respond", new { message = "x" }), HttpStatusCode.Conflict); // nothing asked
        await ReadJson(await AdminPost(admin, reportId, "close", new { outcome = "NoViolation", note = "" }), HttpStatusCode.UnprocessableEntity); // note required
        await ReadJson(await AdminPost(admin, reportId, "close", new { note = "x" }), HttpStatusCode.UnprocessableEntity);  // outcome required
        await ReadJson(await AdminPost(admin, reportId, "request-info", new { message = "Which photo?" }), HttpStatusCode.OK);
        await ReadJson(await AdminPost(admin, reportId, "request-info", new { message = "Again?" }), HttpStatusCode.Conflict);
        await ReadJson(await AdminPost(admin, reportId, "action-required", new { note = "Ask the owner for the deed." }), HttpStatusCode.OK);
        await ReadJson(await AdminPost(admin, reportId, "action-required", new { note = "twice" }), HttpStatusCode.Conflict);
        var priority = await ReadJson(await AdminPost(admin, reportId, "priority", new { priority = "Urgent", reason = "Money involved" }), HttpStatusCode.OK);
        Assert.Equal("Urgent", priority.GetProperty("summary").GetProperty("priority").GetString());
        await ReadJson(await AdminPost(admin, reportId, "close", new { outcome = "CorrectionRequested", note = "Owner will fix the photos." }), HttpStatusCode.OK);
        await ReadJson(await AdminPost(admin, reportId, "priority", new { priority = "Low" }), HttpStatusCode.Conflict); // closed
        await ReadJson(await AdminPost(admin, Guid.NewGuid(), "start-review", new { }), HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "Three different accounts reporting one listing raise every open report to High; nothing is hidden")]
    public async Task Escalation()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var reporter = await _factory.SeedUserAsync($"r{i}");
            var created = await ReadJson(await Report(_factory.AuthedClient(reporter.AccessToken), propertyId, "Duplicate", details: null), HttpStatusCode.Created);
            ids.Add(created.GetProperty("id").GetGuid());
        }

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var reports = await db.ListingReports.Where(r => ids.Contains(r.Id)).ToListAsync();
            Assert.All(reports, r => Assert.Equal(ListingReportPriority.High, r.Priority));
            var property = await db.Properties.SingleAsync(p => p.Id == propertyId);
            Assert.False(property.IsTakenDown);
            Assert.True(property.IsPublished);
        });
    }

    [Fact(DisplayName = "Taken-down or deleted listings: no new reports; existing reports keep their history")]
    public async Task DeletedListing()
    {
        var (owner, reporter, admin, propertyId) = await SeedAsync();
        var reportId = (await ReadJson(await Report(_factory.AuthedClient(reporter.AccessToken), propertyId), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        // The owner deletes the listing (soft delete, as the app does).
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = await db.Properties.SingleAsync(p => p.Id == propertyId);
            db.Properties.Remove(property);
            await db.SaveChangesAsync();
        });

        var detail = await ReadJson(await _factory.AuthedClient(admin.AccessToken).GetAsync($"/api/admin/listing-reports/{reportId}"), HttpStatusCode.OK);
        Assert.True(detail.GetProperty("listing").GetProperty("isDeleted").GetBoolean());
        Assert.True(detail.GetProperty("summary").GetProperty("listingDeleted").GetBoolean());
        // Closing as "taken down" is accepted for a listing that is gone.
        await ReadJson(await AdminPost(admin, reportId, "close", new { outcome = "ListingTakenDown", note = "Owner removed it." }), HttpStatusCode.OK);

        var mine = await ReadJson(await _factory.AuthedClient(reporter.AccessToken).GetAsync("/api/listing-reports/me"), HttpStatusCode.OK);
        Assert.Equal(1, mine.GetProperty("total").GetInt32());

        var other = await _factory.SeedUserAsync("other");
        await ReadJson(await Report(_factory.AuthedClient(other.AccessToken), propertyId), HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "Admin queue: server paging, search by reference, sort by priority, overdue filter")]
    public async Task QueueFilters()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var admin = await _factory.SeedUserAsync("admin", Admin);
        var reasons = new[] { "Duplicate", "Fraud", "IncorrectDetails" };
        var refs = new List<string>();
        foreach (var reason in reasons)
        {
            var reporter = await _factory.SeedUserAsync("r");
            var propertyId = await _factory.SeedPropertyAsync(owner.Id);
            var created = await ReadJson(await Report(_factory.AuthedClient(reporter.AccessToken), propertyId, reason), HttpStatusCode.Created);
            refs.Add(created.GetProperty("referenceNumber").GetString()!);
        }

        var client = _factory.AuthedClient(admin.AccessToken);
        var page1 = await ReadJson(await client.GetAsync("/api/admin/listing-reports?pageSize=2&page=1&sort=Priority"), HttpStatusCode.OK);
        Assert.Equal(3, page1.GetProperty("total").GetInt32());
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.Equal("High", page1.GetProperty("items")[0].GetProperty("priority").GetString());
        var page2 = await ReadJson(await client.GetAsync("/api/admin/listing-reports?pageSize=2&page=2&sort=Priority"), HttpStatusCode.OK);
        Assert.Equal("Low", page2.GetProperty("items")[0].GetProperty("priority").GetString());

        var byRef = await ReadJson(await client.GetAsync($"/api/admin/listing-reports?search={refs[1]}"), HttpStatusCode.OK);
        Assert.Equal(1, byRef.GetProperty("total").GetInt32());

        // Backdate the Duplicate (Low, 240h target) report past its target.
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.ListingReports.Where(r => r.Reason == ListingReportReason.Duplicate)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, DateTime.UtcNow.AddDays(-11)));
        });
        var overdue = await ReadJson(await client.GetAsync("/api/admin/listing-reports?overdue=true"), HttpStatusCode.OK);
        Assert.Equal(1, overdue.GetProperty("total").GetInt32());
        Assert.True(overdue.GetProperty("items")[0].GetProperty("isOverdue").GetBoolean());
        var stats = await ReadJson(await client.GetAsync("/api/admin/listing-reports/stats"), HttpStatusCode.OK);
        Assert.Equal(1, stats.GetProperty("overdue").GetInt32());
        Assert.Equal(3, stats.GetProperty("open").GetInt32());
        Assert.True(stats.GetProperty("averageResolutionHours").ValueKind == JsonValueKind.Null);
    }

    [Fact(DisplayName = "Two simultaneous reports from one account on one listing give one report and one clean 409")]
    public async Task ConcurrentSubmit_OneReport()
    {
        var (_, reporter, _, propertyId) = await SeedAsync();
        var client = _factory.AuthedClient(reporter.AccessToken);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Report(client, propertyId, clientRequestId: Guid.NewGuid())));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await _factory.InScopeAsync(async services =>
            Assert.Equal(1, await services.GetRequiredService<AppDbContext>().ListingReports.CountAsync()));
    }

    [Fact(DisplayName = "Hostile or odd input never causes a 500: wildcards, NUL, huge page size, bad enums, anonymous")]
    public async Task HostileInput()
    {
        var (_, reporter, admin, propertyId) = await SeedAsync();
        var user = _factory.AuthedClient(reporter.AccessToken);
        var adminClient = _factory.AuthedClient(admin.AccessToken);

        foreach (var term in new[] { "%", "_", "\\", "%00", "' OR 1=1 --", new string('x', 500) })
        {
            var res = await adminClient.GetAsync($"/api/admin/listing-reports?search={Uri.EscapeDataString(term)}");
            Assert.True((int)res.StatusCode < 500, $"search '{term}' gave {(int)res.StatusCode}");
        }

        var big = await ReadJson(await adminClient.GetAsync("/api/admin/listing-reports?pageSize=100000&page=1"), HttpStatusCode.OK);
        Assert.True(big.GetProperty("pageSize").GetInt32() <= 100);
        Assert.True((int)(await adminClient.GetAsync("/api/admin/listing-reports?page=-5&pageSize=0")).StatusCode < 500);
        Assert.True((int)(await adminClient.GetAsync("/api/admin/listing-reports?status=Nope&priority=Nope&sort=Nope")).StatusCode < 500);
        Assert.True((int)(await adminClient.GetAsync("/api/admin/listing-reports?from=not-a-date")).StatusCode < 500);

        Assert.True((int)(await Report(user, propertyId, "NotAReason")).StatusCode is >= 400 and < 500);
        Assert.True((int)(await Report(user, propertyId, "Other", details: "bad\u0000text and more text")).StatusCode < 500);
        Assert.True((int)(await Report(user, Guid.Empty)).StatusCode is >= 400 and < 500);

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/listing-reports/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Report(anonymous, propertyId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/listing-reports")).StatusCode);
    }

    [Fact(DisplayName = "A failing notification never loses a report")]
    public async Task NotificationFailure_StillSaves()
    {
        var (_, reporter, admin, propertyId) = await SeedAsync();

        using var failing = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationService>();
            services.AddScoped<INotificationService, ThrowingNotificationService>();
        }));
        var client = failing.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", reporter.AccessToken);

        var created = await ReadJson(await Report(client, propertyId), HttpStatusCode.Created);
        var adminClient = failing.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin.AccessToken);
        await ReadJson(await adminClient.PostAsJsonAsync($"/api/admin/listing-reports/{created.GetProperty("id").GetGuid()}/close",
            new { outcome = "NoViolation", note = "ok" }), HttpStatusCode.OK);

        await _factory.InScopeAsync(async services =>
            Assert.Equal(ListingReportStatus.Dismissed,
                (await services.GetRequiredService<AppDbContext>().ListingReports.SingleAsync()).Status));
    }

    private sealed class ThrowingNotificationService : INotificationService
    {
        private static Task Boom() => throw new InvalidOperationException("notification store down");

        public Task NotifyListingReportAsync(Guid recipientId, NotificationType type, NotificationText message, Guid reportId, Guid? propertyId, string dedupeKey, CancellationToken ct = default) => Boom();

        public Task NotifyInvestmentInterestAsync(Guid recipientId, NotificationText message, Guid investmentProjectId, string dedupeKey, CancellationToken ct = default) => Boom();
        public Task NotifyNewMessageAsync(Guid recipientId, Guid senderId, string senderName, Guid messageId, Guid propertyId, CancellationToken ct = default) => Boom();
        public Task NotifyPropertyUpdateAsync(Guid recipientId, Guid propertyId, string propertyTitle, NotificationType type, NotificationText detail, Guid? relatedEntityId = null, CancellationToken ct = default) => Boom();
        public Task NotifySavedSearchMatchAsync(Guid recipientId, Guid propertyId, string propertyTitle, string savedSearchName, CancellationToken ct = default) => Boom();
        public Task NotifyUserRatedAsync(Guid recipientId, Guid raterId, string raterName, double overallScore, CancellationToken ct = default) => Boom();
        public Task NotifyListingExpiringSoonAsync(Guid recipientId, Guid propertyId, string propertyTitle, int daysRemaining, CancellationToken ct = default) => Boom();
        public Task NotifyListingExpiredAsync(Guid recipientId, Guid propertyId, string propertyTitle, int graceDaysRemaining, CancellationToken ct = default) => Boom();
        public Task NotifyAgencyInvitationReceivedAsync(Guid recipientId, Guid invitationId, string agencyName, string inviterName, CancellationToken ct = default) => Boom();
        public Task NotifyAgencyMemberInquiryAsync(Guid recipientId, Guid messageId, Guid propertyId, string propertyTitle, string agencyName, string customerName, string memberName, CancellationToken ct = default) => Boom();
        public Task NotifyAgencyInvitationRespondedAsync(Guid recipientId, Guid invitationId, string targetUserName, bool accepted, CancellationToken ct = default) => Boom();
        public Task NotifyShortStayBookingUpdateAsync(Guid recipientId, Guid bookingId, string listingTitle, NotificationType type, NotificationText detail, CancellationToken ct = default) => Boom();
        public Task NotifyServiceProviderUpdateAsync(Guid recipientId, NotificationType type, NotificationText message, Guid relatedEntityId, CancellationToken ct = default) => Boom();
        public Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(Guid userId, int page, int pageSize, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default) => Boom();
        public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default) => Boom();
        public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task NotifyValuationInquiryExpiredAsync(Guid recipientId, Guid inquiryId, bool hadPreliminaryEstimate, CancellationToken ct = default) => Boom();
        public Task NotifyValuationOfficeInvitationExpiredAsync(Guid recipientId, Guid invitationId, Guid inquiryId, CancellationToken ct = default) => Boom();
        public Task NotifyValuationResultReadyAsync(Guid recipientId, Guid inquiryId, CancellationToken ct = default) => Boom();
        public Task NotifyValuationInquiryReminderSoonAsync(Guid recipientId, Guid inquiryId, CancellationToken ct = default) => Boom();
        public Task<bool> DeleteNotificationAsync(Guid notificationId, Guid userId, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task<bool> SoftDeleteNotificationAsync(Guid notificationId, Guid userId, CancellationToken ct = default) => throw new InvalidOperationException();
    }
}
