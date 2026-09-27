using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PropertyApi.Integration.Tests.AppUpdates;

namespace PropertyApi.Integration.Tests.Messaging;

/// <summary>
/// End-to-end over real HTTP + real PostgreSQL: a renter contacts a listing owner, the owner
/// finds the inquiry in their inbox and replies, the renter sees the reply, and — only after
/// that real contact — can rate the owner on the multi-axis rating. Every actor id comes from
/// the bearer token; the tests also try to reach other users' data by changing ids in the
/// request and expect nothing to leak.
/// </summary>
[Trait("Feature", "Messaging")]
public sealed class OwnerMessagingAndRatingE2ETests : IClassFixture<MessagingApiTestFactory>, IAsyncLifetime
{
    private readonly MessagingApiTestFactory _factory;

    public OwnerMessagingAndRatingE2ETests(MessagingApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Messaging ────────────────────────────────────────────────────────

    [Fact(DisplayName = "Renter → owner → reply → renter: full thread, inbox and read state")]
    public async Task FullConversation_RoundTrip()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var renter = await _factory.SeedUserAsync("renter");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id, "شقة في المزة");

        var renterClient = _factory.AuthedClient(renter.AccessToken);
        var ownerClient = _factory.AuthedClient(owner.AccessToken);

        // 1. Renter asks — no receiverId: the API routes it to the owner.
        var sent = await renterClient.PostAsJsonAsync("/api/Messages",
            new { propertyId, receiverId = (Guid?)null, content = "  هل الشقة متاحة؟  " });
        await AssertStatus(HttpStatusCode.Created, sent);
        var sentJson = await ReadJson(sent);
        Assert.Equal(renter.Id, sentJson.GetProperty("senderId").GetGuid());
        Assert.Equal(owner.Id, sentJson.GetProperty("receiverId").GetGuid());
        Assert.Equal(propertyId, sentJson.GetProperty("propertyId").GetGuid());
        Assert.Equal("هل الشقة متاحة؟", sentJson.GetProperty("content").GetString());

        // 2. Renter sees it in their own inbox, linked to the right property and owner.
        var renterInbox = await GetInbox(renterClient);
        var renterRow = Assert.Single(renterInbox);
        Assert.Equal(propertyId, renterRow.GetProperty("propertyId").GetGuid());
        Assert.Equal("شقة في المزة", renterRow.GetProperty("propertyTitle").GetString());
        Assert.Equal(owner.Id, renterRow.GetProperty("otherUserId").GetGuid());
        Assert.False(renterRow.GetProperty("isPropertyOwner").GetBoolean());
        Assert.Equal(0, renterRow.GetProperty("unreadCount").GetInt32());

        // 3. Owner sees the inquiry as unread, marked as one of their own listings.
        var ownerInbox = await GetInbox(ownerClient);
        var ownerRow = Assert.Single(ownerInbox);
        Assert.Equal(renter.Id, ownerRow.GetProperty("otherUserId").GetGuid());
        Assert.True(ownerRow.GetProperty("isPropertyOwner").GetBoolean());
        Assert.Equal(1, ownerRow.GetProperty("unreadCount").GetInt32());
        Assert.Equal("هل الشقة متاحة؟", ownerRow.GetProperty("lastMessageContent").GetString());

        // 4. Owner opens the thread (the page size the frontend now uses) and marks it read.
        var ownerThread = await GetThread(ownerClient, propertyId, renter.Id);
        Assert.Single(ownerThread);
        await MarkRead(ownerClient, propertyId, renter.Id, expectedUpdated: 1);
        Assert.Equal(0, Assert.Single(await GetInbox(ownerClient)).GetProperty("unreadCount").GetInt32());

        // 5. Owner replies to the renter in the same conversation.
        var reply = await ownerClient.PostAsJsonAsync("/api/Messages",
            new { propertyId, receiverId = renter.Id, content = "نعم، متاحة من الشهر القادم." });
        await AssertStatus(HttpStatusCode.Created, reply);

        // 6. Renter sees the reply, unread, and the full thread in order.
        var renterRowAfterReply = Assert.Single(await GetInbox(renterClient));
        Assert.Equal(1, renterRowAfterReply.GetProperty("unreadCount").GetInt32());
        Assert.Equal(owner.Id, renterRowAfterReply.GetProperty("lastMessageSenderId").GetGuid());

        // 7. Renter answers back from the same conversation (no receiverId needed).
        var followUp = await renterClient.PostAsJsonAsync("/api/Messages",
            new { propertyId, receiverId = (Guid?)null, content = "ممتاز، متى يمكنني الزيارة؟" });
        await AssertStatus(HttpStatusCode.Created, followUp);

        var renterThread = await GetThread(renterClient, propertyId, owner.Id);
        Assert.Equal(3, renterThread.Count);
        var chronological = renterThread
            .OrderBy(m => m.GetProperty("createdAt").GetDateTime())
            .Select(m => m.GetProperty("senderId").GetGuid())
            .ToList();
        Assert.Equal(new[] { renter.Id, owner.Id, renter.Id }, chronological);

        await MarkRead(renterClient, propertyId, owner.Id, expectedUpdated: 1);
        Assert.Equal(0, Assert.Single(await GetInbox(renterClient)).GetProperty("unreadCount").GetInt32());
    }

    [Fact(DisplayName = "Inbox groups per property + counterpart, newest first, and filters by property")]
    public async Task Inbox_MultipleProperties()
    {
        var ownerA = await _factory.SeedUserAsync("owner-a");
        var ownerB = await _factory.SeedUserAsync("owner-b");
        var renter = await _factory.SeedUserAsync("renter");
        var otherRenter = await _factory.SeedUserAsync("renter-2");
        var propertyA = await _factory.SeedPropertyAsync(ownerA.Id, "عقار أ");
        var propertyB = await _factory.SeedPropertyAsync(ownerB.Id, "عقار ب");
        var propertyA2 = await _factory.SeedPropertyAsync(ownerA.Id, "عقار أ2");

        var renterClient = _factory.AuthedClient(renter.AccessToken);
        await Send(renterClient, propertyA, "سؤال عن أ");
        await Send(renterClient, propertyA, "سؤال ثانٍ عن أ");
        await Send(renterClient, propertyB, "سؤال عن ب");
        await Send(_factory.AuthedClient(otherRenter.AccessToken), propertyA2, "سؤال آخر");

        var renterInbox = await GetInbox(renterClient);
        Assert.Equal(2, renterInbox.Count);
        Assert.Equal(propertyB, renterInbox[0].GetProperty("propertyId").GetGuid());
        Assert.Equal(propertyA, renterInbox[1].GetProperty("propertyId").GetGuid());
        Assert.Equal("سؤال ثانٍ عن أ", renterInbox[1].GetProperty("lastMessageContent").GetString());

        var ownerAClient = _factory.AuthedClient(ownerA.AccessToken);
        var ownerAInbox = await GetInbox(ownerAClient);
        Assert.Equal(2, ownerAInbox.Count);
        Assert.Equal(2, ownerAInbox.Single(r => r.GetProperty("propertyId").GetGuid() == propertyA)
            .GetProperty("unreadCount").GetInt32());

        var filtered = await GetInbox(ownerAClient, propertyA2);
        var only = Assert.Single(filtered);
        Assert.Equal(otherRenter.Id, only.GetProperty("otherUserId").GetGuid());
    }

    [Fact(DisplayName = "A third user cannot read or mark read another pair's conversation by changing ids")]
    public async Task ThirdUser_CannotReachOthersConversation()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var renter = await _factory.SeedUserAsync("renter");
        var intruder = await _factory.SeedUserAsync("intruder");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id);

        await Send(_factory.AuthedClient(renter.AccessToken), propertyId, "رسالة خاصة");

        var intruderClient = _factory.AuthedClient(intruder.AccessToken);

        Assert.Empty(await GetInbox(intruderClient));
        Assert.Empty(await GetInbox(intruderClient, propertyId));

        // Asking for "the conversation with the renter" / "with the owner" only ever returns
        // messages the intruder is a party to — none.
        Assert.Empty(await GetThread(intruderClient, propertyId, renter.Id));
        Assert.Empty(await GetThread(intruderClient, propertyId, owner.Id));

        // Listing all of the property's messages is owner-only.
        var all = await intruderClient.GetAsync($"/api/Messages?propertyId={propertyId}&page=1&pageSize=50");
        await AssertStatus(HttpStatusCode.Forbidden, all);

        // Marking "renter's messages" read only touches messages addressed to the caller: nothing.
        await MarkRead(intruderClient, propertyId, renter.Id, expectedUpdated: 0);
        Assert.Equal(1, Assert.Single(await GetInbox(_factory.AuthedClient(owner.AccessToken)))
            .GetProperty("unreadCount").GetInt32());

        // Unauthenticated callers get nothing at all.
        var anonymous = await _factory.CreateClient().GetAsync("/api/Messages/conversations");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact(DisplayName = "Invalid property, self-messaging and cold owner messages are rejected")]
    public async Task InvalidSends_AreRejected()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var stranger = await _factory.SeedUserAsync("stranger");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id);
        var ownerClient = _factory.AuthedClient(owner.AccessToken);

        var unknownProperty = await _factory.AuthedClient(stranger.AccessToken).PostAsJsonAsync("/api/Messages",
            new { propertyId = Guid.NewGuid(), receiverId = (Guid?)null, content = "مرحبا" });
        await AssertStatus(HttpStatusCode.NotFound, unknownProperty);

        // Owner writing on their own listing without choosing a counterpart = messaging themself.
        var noReceiver = await ownerClient.PostAsJsonAsync("/api/Messages",
            new { propertyId, receiverId = (Guid?)null, content = "مرحبا" });
        await AssertStatus(HttpStatusCode.UnprocessableEntity, noReceiver);

        var toSelf = await ownerClient.PostAsJsonAsync("/api/Messages",
            new { propertyId, receiverId = owner.Id, content = "مرحبا" });
        Assert.True(toSelf.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.UnprocessableEntity,
            $"Expected 403/422, got {toSelf.StatusCode}");

        // Owners can only reply — they cannot cold-message arbitrary users about a listing.
        var cold = await ownerClient.PostAsJsonAsync("/api/Messages",
            new { propertyId, receiverId = stranger.Id, content = "إعلان" });
        await AssertStatus(HttpStatusCode.Forbidden, cold);

        var empty = await _factory.AuthedClient(stranger.AccessToken).PostAsJsonAsync("/api/Messages",
            new { propertyId, receiverId = (Guid?)null, content = "   " });
        await AssertStatus(HttpStatusCode.UnprocessableEntity, empty);

        Assert.Empty(await GetInbox(ownerClient));
    }

    // ── Owner rating ─────────────────────────────────────────────────────

    [Fact(DisplayName = "Rating requires real contact; self and out-of-range ratings are rejected")]
    public async Task Rating_Eligibility()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var renter = await _factory.SeedUserAsync("renter");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id);
        var renterClient = _factory.AuthedClient(renter.AccessToken);
        var ownerClient = _factory.AuthedClient(owner.AccessToken);

        // Just viewing the listing is not enough.
        var before = await ReadJson(await renterClient.GetAsync($"/api/Users/{owner.Id}/ratings/eligibility"));
        Assert.False(before.GetProperty("canRate").GetBoolean());
        var early = await renterClient.PostAsJsonAsync($"/api/Users/{owner.Id}/ratings", FullRating(5));
        await AssertStatus(HttpStatusCode.BadRequest, early);

        await Send(renterClient, propertyId, "هل السعر قابل للتفاوض؟");

        var after = await ReadJson(await renterClient.GetAsync($"/api/Users/{owner.Id}/ratings/eligibility"));
        Assert.True(after.GetProperty("canRate").GetBoolean());
        Assert.False(after.GetProperty("alreadyRated").GetBoolean());

        var self = await ReadJson(await ownerClient.GetAsync($"/api/Users/{owner.Id}/ratings/eligibility"));
        Assert.True(self.GetProperty("isSelf").GetBoolean());
        Assert.False(self.GetProperty("canRate").GetBoolean());
        var selfRate = await ownerClient.PostAsJsonAsync($"/api/Users/{owner.Id}/ratings", FullRating(5));
        await AssertStatus(HttpStatusCode.BadRequest, selfRate);

        var outOfRange = await renterClient.PostAsJsonAsync($"/api/Users/{owner.Id}/ratings",
            new { credibility = 5, safety = 5, responseSpeed = 5, transparency = 5, informationAccuracy = 6, conduct = 5 });
        await AssertStatus(HttpStatusCode.UnprocessableEntity, outOfRange);

        var tooLong = await renterClient.PostAsJsonAsync($"/api/Users/{owner.Id}/ratings",
            new { credibility = 5, safety = 5, responseSpeed = 5, transparency = 5, comment = new string('x', 1001) });
        await AssertStatus(HttpStatusCode.UnprocessableEntity, tooLong);

        var summary = await ReadJson(await renterClient.GetAsync($"/api/Users/{owner.Id}/ratings"));
        Assert.Equal(0, summary.GetProperty("totalCount").GetInt32());
    }

    [Fact(DisplayName = "Multi-axis rating: upsert keeps one rating per pair; averages computed server-side")]
    public async Task Rating_MultiAxis_AndAverages()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var renterA = await _factory.SeedUserAsync("renter-a");
        var renterB = await _factory.SeedUserAsync("renter-b");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id);

        var clientA = _factory.AuthedClient(renterA.AccessToken);
        var clientB = _factory.AuthedClient(renterB.AccessToken);
        await Send(clientA, propertyId, "سؤال");
        await Send(clientB, propertyId, "سؤال");

        // A: all six axes. Overall of this single rating = (5+4+3+4+5+3)/6 = 4.0
        var first = await clientA.PostAsJsonAsync($"/api/Users/{owner.Id}/ratings", new
        {
            credibility = 5,
            safety = 4,
            responseSpeed = 3,
            transparency = 4,
            informationAccuracy = 5,
            conduct = 3,
            comment = "  تعامل جيد  "
        });
        await AssertStatus(HttpStatusCode.Created, first);
        var firstJson = await ReadJson(first);
        Assert.Equal(4.0, firstJson.GetProperty("overallScore").GetDouble(), 3);
        Assert.Equal(5, firstJson.GetProperty("informationAccuracy").GetInt32());
        Assert.Equal("تعامل جيد", firstJson.GetProperty("comment").GetString());

        // A edits instead of adding a second rating: (5+5+5+5+5+5)/6 = 5.0
        var edit = await clientA.PostAsJsonAsync($"/api/Users/{owner.Id}/ratings", FullRating(5));
        await AssertStatus(HttpStatusCode.Created, edit);
        var eligibility = await ReadJson(await clientA.GetAsync($"/api/Users/{owner.Id}/ratings/eligibility"));
        Assert.True(eligibility.GetProperty("alreadyRated").GetBoolean());
        Assert.Equal(5, eligibility.GetProperty("existingRating").GetProperty("conduct").GetInt32());

        // B: an older client that only knows the four core axes. Overall = (2+2+2+2)/4 = 2.0
        var legacy = await clientB.PostAsJsonAsync($"/api/Users/{owner.Id}/ratings",
            new { credibility = 2, safety = 2, responseSpeed = 2, transparency = 2 });
        await AssertStatus(HttpStatusCode.Created, legacy);
        var legacyJson = await ReadJson(legacy);
        Assert.Equal(2.0, legacyJson.GetProperty("overallScore").GetDouble(), 3);
        Assert.Equal(JsonValueKind.Null, legacyJson.GetProperty("conduct").ValueKind);

        var summary = await ReadJson(await clientB.GetAsync($"/api/Users/{owner.Id}/ratings"));
        Assert.Equal(2, summary.GetProperty("totalCount").GetInt32());
        Assert.Equal(3.5, summary.GetProperty("averageCredibility").GetDouble(), 3);  // (5+2)/2
        // Optional axes average only the ratings that scored them — B's null is not a 0.
        Assert.Equal(5.0, summary.GetProperty("averageInformationAccuracy").GetDouble(), 3);
        Assert.Equal(5.0, summary.GetProperty("averageConduct").GetDouble(), 3);
        // Overall = mean of each rating's own overall = (5.0 + 2.0) / 2
        Assert.Equal(3.5, summary.GetProperty("averageOverall").GetDouble(), 3);

        var profile = await ReadJson(await _factory.CreateClient().GetAsync($"/api/Users/{owner.Id}/profile"));
        Assert.Equal(2, profile.GetProperty("ratingsCount").GetInt32());
        Assert.Equal(3.5, profile.GetProperty("averageOverall").GetDouble(), 3);
        Assert.Equal(5.0, profile.GetProperty("averageConduct").GetDouble(), 3);
    }

    [Fact(DisplayName = "Rating summary with no optional-axis data reports them as null")]
    public async Task Rating_OptionalAxes_NullWhenUnrated()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var renter = await _factory.SeedUserAsync("renter");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id);
        var client = _factory.AuthedClient(renter.AccessToken);
        await Send(client, propertyId, "سؤال");

        await AssertStatus(HttpStatusCode.Created, await client.PostAsJsonAsync($"/api/Users/{owner.Id}/ratings",
            new { credibility = 4, safety = 4, responseSpeed = 4, transparency = 4 }));

        var summary = await ReadJson(await client.GetAsync($"/api/Users/{owner.Id}/ratings"));
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("averageInformationAccuracy").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("averageConduct").ValueKind);
        Assert.Equal(4.0, summary.GetProperty("averageOverall").GetDouble(), 3);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static object FullRating(int score) => new
    {
        credibility = score,
        safety = score,
        responseSpeed = score,
        transparency = score,
        informationAccuracy = score,
        conduct = score
    };

    private static async Task Send(HttpClient client, Guid propertyId, string content)
    {
        var response = await client.PostAsJsonAsync("/api/Messages",
            new { propertyId, receiverId = (Guid?)null, content });
        await AssertStatus(HttpStatusCode.Created, response);
    }

    private static async Task<List<JsonElement>> GetInbox(HttpClient client, Guid? propertyId = null)
    {
        var url = "/api/Messages/conversations?page=1&pageSize=20"
            + (propertyId.HasValue ? $"&propertyId={propertyId}" : string.Empty);
        var response = await client.GetAsync(url);
        await AssertStatus(HttpStatusCode.OK, response);
        var json = await ReadJson(response);
        return json.GetProperty("items").EnumerateArray().ToList();
    }

    private static async Task<List<JsonElement>> GetThread(HttpClient client, Guid propertyId, Guid otherUserId)
    {
        var response = await client.GetAsync(
            $"/api/Messages?propertyId={propertyId}&otherUserId={otherUserId}&page=1&pageSize=50");
        await AssertStatus(HttpStatusCode.OK, response);
        var json = await ReadJson(response);
        return json.GetProperty("items").EnumerateArray().ToList();
    }

    private static async Task MarkRead(HttpClient client, Guid propertyId, Guid otherUserId, int expectedUpdated)
    {
        var response = await client.PostAsJsonAsync("/api/Messages/conversations/read",
            new { propertyId, otherUserId });
        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(expectedUpdated, (await ReadJson(response)).GetProperty("updated").GetInt32());
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task AssertStatus(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}: {body}");
        }
    }
}
