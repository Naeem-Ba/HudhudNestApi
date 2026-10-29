using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HudhudNestApi.Application.Listings.Translations;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Listings.Translations;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings.Translations;

/// <summary>
/// Automatic listing translation against a real PostgreSQL and the real API host. The provider is a
/// recording fake (no key, no network): these tests prove OUR behaviour - storage, reuse, invalidation,
/// concurrency, deletion, failure and budget handling - not the quality or availability of any vendor.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "ListingTranslation")]
public sealed class ListingTranslationPostgresTests : IAsyncLifetime
{
    private const string ArabicTitle = "شقة مفروشة في المزة";
    private const string ArabicDescription = "شقة واسعة ومشرقة في الطابق الثالث مع بلكون وإطلالة على المدينة.";

    private readonly PostgresAuthTestFactory _baseFactory = new();
    private readonly FakeProvider _provider = new();
    private readonly string _marker = $"lt{Guid.NewGuid():N}";
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _ownerId;

    public async Task InitializeAsync()
    {
        await _baseFactory.PrepareDatabaseAsync();
        _ownerId = (await _baseFactory.SeedUserAsync($"{_marker}@test.local")).UserAccountId;
        _factory = Build(_provider);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        _baseFactory.Dispose();
        return Task.CompletedTask;
    }

    private WebApplicationFactory<Program> Build(FakeProvider provider, Dictionary<string, string?>? settings = null) =>
        _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>()));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITranslationProvider>();
                services.AddSingleton<ITranslationProvider>(provider);
            });
        });

    private async Task<Guid> SeedAsync(
        string title = ArabicTitle, string description = ArabicDescription, bool published = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var property = Property.Create(title, description, _ownerId, ListingType.ForSale, isPublished: published);
        property.PurchasePrice = 125_000m;
        property.CurrencyCode = "USD";
        property.Rooms = 3;
        property.Area = 140m;
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        return property.Id;
    }

    private Task<ListingTranslationDto?> EnsureAsync(WebApplicationFactory<Program> factory, Guid id, string lang) =>
        EnsureCoreAsync(factory, id, lang);

    private static async Task<ListingTranslationDto?> EnsureCoreAsync(WebApplicationFactory<Program> factory, Guid id, string lang)
    {
        // A fresh scope per call is exactly "another user, another device": nothing is shared but the database.
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IListingTranslationService>()
            .EnsureAsync(id, lang, CancellationToken.None);
    }

    private Task<ListingTranslationDto?> EnsureAsync(Guid id, string lang) => EnsureAsync(_factory, id, lang);

    private async Task EditAsync(Guid id, Action<Property> edit)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var property = await db.Properties.SingleAsync(p => p.Id == id);
        edit(property);
        await db.SaveChangesAsync();
    }

    private async Task<List<ListingTranslation>> RowsAsync(Guid? id = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var query = db.ListingTranslations.IgnoreQueryFilters().AsNoTracking();
        if (id is { } propertyId) query = query.Where(t => t.PropertyId == propertyId);
        return await query.OrderBy(t => t.Field).ThenBy(t => t.TargetLanguage).ToListAsync();
    }

    private async Task<int> ExecAsync(string sql, params object[] parameters)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    // ── Core behaviour ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "Opening an Arabic listing in English creates one stored translation with a single provider call")]
    public async Task FirstOpen_TranslatesBothFields_WithOneProviderCall()
    {
        var id = await SeedAsync();

        var dto = await EnsureAsync(id, "en");

        Assert.NotNull(dto);
        Assert.Equal("ready", dto!.Status);
        Assert.Equal($"[en] {ArabicTitle}", dto.Title.Text);
        Assert.Equal($"[en] {ArabicDescription}", dto.Description.Text);
        Assert.Equal("ar", dto.Title.SourceLanguage);
        Assert.Equal(1, _provider.Calls);
        Assert.Equal("ar", _provider.Log.Single().Source);

        var rows = await RowsAsync(id);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(ListingTranslationStatus.Completed, r.Status));
        Assert.All(rows, r => Assert.Equal("fake", r.Provider));
        Assert.All(rows, r => Assert.Null(r.ClaimToken));
    }

    [Fact(DisplayName = "A stored translation is reused by other users and after a server restart, without calling the provider")]
    public async Task StoredTranslation_IsReused_AcrossUsersAndRestarts()
    {
        var id = await SeedAsync();
        await EnsureAsync(id, "en");
        Assert.Equal(1, _provider.Calls);

        var other = await EnsureAsync(id, "en");
        Assert.Equal("ready", other!.Status);

        // A brand-new host over the same database = a restarted/second server instance with its own provider.
        var restartedProvider = new FakeProvider();
        using var restarted = Build(restartedProvider);
        var afterRestart = await EnsureAsync(restarted, id, "en");

        Assert.Equal("ready", afterRestart!.Status);
        Assert.Equal($"[en] {ArabicTitle}", afterRestart.Title.Text);
        Assert.Equal(1, _provider.Calls);
        Assert.Equal(0, restartedProvider.Calls);
    }

    [Fact(DisplayName = "Each target language gets its own translation; same-language requests never call the provider")]
    public async Task EachLanguage_IsSeparate_AndSameLanguageIsFree()
    {
        var id = await SeedAsync();

        Assert.Equal("ready", (await EnsureAsync(id, "en"))!.Status);
        Assert.Equal("ready", (await EnsureAsync(id, "de"))!.Status);
        Assert.Equal(2, _provider.Calls);

        var arabic = await EnsureAsync(id, "ar");
        Assert.Equal("same_language", arabic!.Status);
        Assert.Equal(2, _provider.Calls);
        Assert.Equal(4, (await RowsAsync(id)).Count);
    }

    [Fact(DisplayName = "English and German sources translate to the other languages, and English to English is skipped")]
    public async Task LatinSources_AreDetected()
    {
        var english = await SeedAsync("Furnished apartment for rent in Mazzeh", "A spacious and quiet apartment with a balcony and a view.");
        var german = await SeedAsync("Möblierte Wohnung zu vermieten in Mazzeh", "Eine helle und ruhige Wohnung mit Balkon und Aufzug in der Nähe.");

        Assert.Equal("same_language", (await EnsureAsync(english, "en"))!.Status);
        Assert.Equal("ready", (await EnsureAsync(english, "de"))!.Status);
        Assert.Equal("ready", (await EnsureAsync(english, "ar"))!.Status);
        Assert.Equal("same_language", (await EnsureAsync(german, "de"))!.Status);
        Assert.Equal("ready", (await EnsureAsync(german, "en"))!.Status);
        Assert.Equal("ready", (await EnsureAsync(german, "ar"))!.Status);

        Assert.Contains(_provider.Log, c => c.Source == "en" && c.Target == "de");
        Assert.Contains(_provider.Log, c => c.Source == "de" && c.Target == "en");
    }

    [Fact(DisplayName = "Translating never changes the listing's own data")]
    public async Task Translation_LeavesStructuredDataAndOriginalTextUntouched()
    {
        var id = await SeedAsync();
        await EnsureAsync(id, "en");
        await EnsureAsync(id, "de");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var p = await db.Properties.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(ArabicTitle, p.Title);
        Assert.Equal(ArabicDescription, p.Description);
        Assert.Equal(125_000m, p.PurchasePrice);
        Assert.Equal("USD", p.CurrencyCode);
        Assert.Equal(3, p.Rooms);
        Assert.Equal(140m, p.Area);
        // Only the text fields were ever sent out.
        Assert.All(_provider.Log, c => Assert.All(c.Texts, t => Assert.True(t == ArabicTitle || t == ArabicDescription)));
    }

    // ── Edits ────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Editing the title re-translates only the title; the description translation is kept")]
    public async Task EditTitle_InvalidatesOnlyTheTitle()
    {
        var id = await SeedAsync();
        var first = await EnsureAsync(id, "en");
        var descriptionRowBefore = (await RowsAsync(id)).Single(r => r.Field == ListingTranslationField.Description);

        await EditAsync(id, p => p.UpdateTitle("شقة مفروشة جديدة في المالكي"));
        var stale = _provider.Calls;
        var second = await EnsureAsync(id, "en");

        Assert.Equal("[en] شقة مفروشة جديدة في المالكي", second!.Title.Text);
        Assert.NotEqual(first!.Title.Text, second.Title.Text);
        Assert.Equal(first.Description.Text, second.Description.Text);
        Assert.Equal(stale + 1, _provider.Calls);
        Assert.Equal(new[] { "شقة مفروشة جديدة في المالكي" }, _provider.Log.Last().Texts);

        var descriptionRowAfter = (await RowsAsync(id)).Single(r => r.Field == ListingTranslationField.Description);
        Assert.Equal(descriptionRowBefore.UpdatedAt, descriptionRowAfter.UpdatedAt);
        Assert.Equal(2, (await RowsAsync(id)).Count); // same rows, updated in place - no orphans or stale copies
    }

    [Fact(DisplayName = "Editing the description re-translates only the description on the next request")]
    public async Task EditDescription_InvalidatesOnlyTheDescription()
    {
        var id = await SeedAsync();
        var first = await EnsureAsync(id, "de");

        await EditAsync(id, p => p.UpdateDescription("وصف جديد كامل للشقة بعد التجديد."));
        var calls = _provider.Calls;

        // Nothing happens at save time: the new translation is made by the next request.
        Assert.Equal(calls, _provider.Calls);
        var second = await EnsureAsync(id, "de");

        Assert.Equal(first!.Title.Text, second!.Title.Text);
        Assert.Equal("[de] وصف جديد كامل للشقة بعد التجديد.", second.Description.Text);
        Assert.Equal(calls + 1, _provider.Calls);
    }

    [Fact(DisplayName = "A stale translation is never served even if no invalidation ran")]
    public async Task StaleRow_IsNeverServed()
    {
        var id = await SeedAsync();
        await EnsureAsync(id, "en");
        _provider.Behavior = (_, _, _, _) => Task.FromResult(TranslationBatchResult.Fail(TranslationFailureKind.Transient));

        await EditAsync(id, p => p.UpdateTitle("عنوان مختلف تمامًا للإعلان"));
        var dto = await EnsureAsync(id, "en");

        Assert.NotEqual("[en] " + ArabicTitle, dto!.Title.Text);
        Assert.Null(dto.Title.Text);
        Assert.Equal("unavailable", dto.Title.State);
    }

    [Fact(DisplayName = "A policy-version bump invalidates every stored translation")]
    public async Task PolicyVersionBump_Invalidates()
    {
        var id = await SeedAsync();
        await EnsureAsync(id, "en");

        using var bumped = Build(_provider, new() { ["ListingTranslation:PolicyVersion"] = "2" });
        var dto = await EnsureAsync(bumped, id, "en");

        Assert.Equal("ready", dto!.Status);
        Assert.Equal(2, _provider.Calls);
        Assert.All(await RowsAsync(id), r => Assert.Equal(2, r.PolicyVersion));
    }

    // ── Concurrency ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Many simultaneous first opens cause exactly one provider call")]
    public async Task ConcurrentOpens_CallTheProviderOnce()
    {
        var id = await SeedAsync();
        _provider.Behavior = async (texts, src, target, ct) =>
        {
            await Task.Delay(400, ct); // long enough for every racer to arrive while the claim is held
            return FakeProvider.Echo(texts, src, target);
        };

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => EnsureAsync(id, "en")));

        Assert.Equal(1, _provider.Calls);
        Assert.All(results, r => Assert.Contains(r!.Status, new[] { "ready", "pending" }));
        Assert.Contains(results, r => r!.Status == "ready");

        var settled = await EnsureAsync(id, "en");
        Assert.Equal("ready", settled!.Status);
        Assert.Equal(1, _provider.Calls);
    }

    [Fact(DisplayName = "Two server instances racing on the same listing still cost one provider call")]
    public async Task TwoInstances_DoNotDuplicateWork()
    {
        var id = await SeedAsync();
        var providerB = new FakeProvider();
        using var instanceB = Build(providerB);
        Task<TranslationBatchResult> Slow(IReadOnlyList<string> t, string? s, string tg, CancellationToken ct) =>
            Task.Delay(400, ct).ContinueWith(_ => FakeProvider.Echo(t, s, tg), ct);
        _provider.Behavior = Slow;
        providerB.Behavior = Slow;

        var tasks = Enumerable.Range(0, 10).Select(i => EnsureAsync(i % 2 == 0 ? _factory : instanceB, id, "de"));
        await Task.WhenAll(tasks);

        Assert.Equal(1, _provider.Calls + providerB.Calls);
    }

    [Fact(DisplayName = "A live claim by someone else is reported as pending without a provider call; an abandoned claim is taken over")]
    public async Task Claims_AreRespected_AndAbandonedOnesRecovered()
    {
        var id = await SeedAsync();
        await EnsureAsync(id, "en"); // creates rows

        // Make the title row look in-flight on another instance.
        await ExecAsync(
            "UPDATE \"ListingTranslations\" SET \"Status\" = 1, \"TranslatedText\" = NULL, \"ClaimToken\" = {0}, \"ClaimedUntil\" = {1} WHERE \"PropertyId\" = {2} AND \"Field\" = 1",
            Guid.NewGuid(), DateTime.UtcNow.AddMinutes(1), id);
        var calls = _provider.Calls;

        var pending = await EnsureAsync(id, "en");
        Assert.Equal("pending", pending!.Status);
        Assert.Equal(2, pending.RetryAfterSeconds);
        Assert.Equal(calls, _provider.Calls);

        // The other instance died: its lease is in the past, so it can be taken over (never stuck forever).
        await ExecAsync("UPDATE \"ListingTranslations\" SET \"ClaimedUntil\" = {0} WHERE \"PropertyId\" = {1} AND \"Field\" = 1",
            DateTime.UtcNow.AddMinutes(-1), id);
        var recovered = await EnsureAsync(id, "en");

        Assert.Equal("ready", recovered!.Status);
        Assert.Equal($"[en] {ArabicTitle}", recovered.Title.Text);
        Assert.Equal(calls + 1, _provider.Calls);
    }

    // ── Source changes / deletion during translation ─────────────────────────────

    [Fact(DisplayName = "Text edited while the provider is working: the old result is discarded and the next request translates the new text")]
    public async Task EditDuringTranslation_DiscardsTheStaleResult()
    {
        var id = await SeedAsync();
        _provider.Behavior = async (texts, src, target, ct) =>
        {
            await EditAsync(id, p => p.UpdateTitle("عنوان تم تعديله أثناء الترجمة"));
            return FakeProvider.Echo(texts, src, target);
        };

        var during = await EnsureAsync(id, "en");

        Assert.Equal("pending", during!.Title.State); // title result refused; description (unchanged) was stored
        Assert.Equal("ready", during.Description.State);
        var rows = await RowsAsync(id);
        Assert.DoesNotContain(rows, r => r.Field == ListingTranslationField.Title && r.Status == ListingTranslationStatus.Completed);

        _provider.Behavior = null;
        var next = await EnsureAsync(id, "en");
        Assert.Equal("[en] عنوان تم تعديله أثناء الترجمة", next!.Title.Text);
    }

    [Fact(DisplayName = "Listing deleted while the provider is working: nothing is stored")]
    public async Task DeleteDuringTranslation_StoresNothing()
    {
        var id = await SeedAsync();
        _provider.Behavior = async (texts, src, target, ct) =>
        {
            await EditAsync(id, p => p.MarkAsDeleted(_ownerId));
            return FakeProvider.Echo(texts, src, target);
        };

        var dto = await EnsureAsync(id, "en");

        Assert.NotEqual("ready", dto!.Status);
        Assert.DoesNotContain(await RowsAsync(id), r => r.Status == ListingTranslationStatus.Completed);
        Assert.Null(await EnsureAsync(id, "en")); // gone for everybody now
    }

    [Fact(DisplayName = "A soft-deleted, unpublished or unknown listing yields nothing and never reaches the provider")]
    public async Task UnavailableListings_AreNotFound()
    {
        var soft = await SeedAsync();
        await EditAsync(soft, p => p.MarkAsDeleted(_ownerId));
        var unpublished = await SeedAsync(published: false);

        Assert.Null(await EnsureAsync(soft, "en"));
        Assert.Null(await EnsureAsync(unpublished, "en"));
        Assert.Null(await EnsureAsync(Guid.NewGuid(), "en"));
        Assert.Equal(0, _provider.Calls);
    }

    [Fact(DisplayName = "Soft delete hides the listing and its translations; a permanent delete removes them, and only them")]
    public async Task SoftAndHardDelete_FollowTheListingPolicy()
    {
        var doomed = await SeedAsync();
        var survivor = await SeedAsync("شقة أخرى للبيع في دمشق", "وصف آخر لشقة أخرى في مكان آخر من المدينة.");
        await EnsureAsync(doomed, "en");
        await EnsureAsync(doomed, "de");
        await EnsureAsync(survivor, "en");
        Assert.Equal(6, (await RowsAsync()).Count);

        // Soft delete (the only delete the application performs): unreachable through the API and through EF filters.
        await EditAsync(doomed, p => p.MarkAsDeleted(_ownerId));
        Assert.Null(await EnsureAsync(doomed, "en"));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.ListingTranslations.Where(t => t.PropertyId == doomed).ToListAsync());
        }
        Assert.Equal(4, (await RowsAsync(doomed)).Count); // kept with the (soft-deleted) listing, like its own text

        // Permanent delete: the foreign key cascade removes every language and field of that listing only.
        await ExecAsync("DELETE FROM \"Properties\" WHERE \"Id\" = {0}", doomed);
        Assert.Empty(await RowsAsync(doomed));
        Assert.Equal(2, (await RowsAsync(survivor)).Count);
        Assert.Equal("ready", (await EnsureAsync(survivor, "en"))!.Status);
    }

    [Fact(DisplayName = "The database refuses a translation row for a missing listing, a blank success, or an unknown language")]
    public async Task Database_RefusesInvalidRows()
    {
        var id = await SeedAsync();
        var hash = new string('a', 64);
        const string insert =
            "INSERT INTO \"ListingTranslations\" (\"Id\",\"PropertyId\",\"Field\",\"TargetLanguage\",\"SourceHash\",\"PolicyVersion\",\"Provider\",\"Status\",\"TranslatedText\",\"SourceCharacters\",\"AttemptCount\",\"CreatedAt\",\"UpdatedAt\") " +
            "VALUES ({0},{1},1,{2},{3},1,'x',{4},{5},1,0,now(),now())";

        await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(insert, Guid.NewGuid(), Guid.NewGuid(), "en", hash, 2, "text")); // orphan
        await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(insert, Guid.NewGuid(), id, "en", hash, 2, "   "));            // blank success
        await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(insert, Guid.NewGuid(), id, "fr", hash, 2, "texte"));           // language
        await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(insert, Guid.NewGuid(), id, "en", hash, 4, "text"));            // text on a failure
        Assert.Equal(1, await ExecAsync(insert, Guid.NewGuid(), id, "en", hash, 2, "ok"));
        await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(insert, Guid.NewGuid(), id, "en", hash, 2, "duplicate key"));  // unique
    }

    // ── Failures, time-outs, budget ──────────────────────────────────────────────

    [Fact(DisplayName = "Provider failure shows the original, stores no success, and retries are bounded by cooldowns")]
    public async Task ProviderFailure_IsBounded()
    {
        var id = await SeedAsync();
        using var fast = Build(_provider, new()
        {
            ["ListingTranslation:ShortCooldownSeconds"] = "1",
            ["ListingTranslation:MaxAttempts"] = "3",
            ["ListingTranslation:LongCooldownMinutes"] = "60",
        });
        _provider.Behavior = (_, _, _, _) => Task.FromResult(TranslationBatchResult.Fail(TranslationFailureKind.Transient));

        var first = await EnsureAsync(fast, id, "en");
        Assert.Equal("unavailable", first!.Status);
        Assert.Equal("provider_error", first.Reason);
        Assert.DoesNotContain(await RowsAsync(id), r => r.Status == ListingTranslationStatus.Completed);
        Assert.Equal(1, _provider.Calls);

        // Hammering during the cooldown costs nothing.
        for (var i = 0; i < 20; i++) await EnsureAsync(fast, id, "en");
        Assert.Equal(1, _provider.Calls);

        await Task.Delay(1200);
        await EnsureAsync(fast, id, "en"); // attempt 2
        Assert.Equal(2, _provider.Calls);
        await Task.Delay(2200);
        await EnsureAsync(fast, id, "en"); // attempt 3 -> long cooldown
        Assert.Equal(3, _provider.Calls);
        await Task.Delay(2500);
        for (var i = 0; i < 5; i++) await EnsureAsync(fast, id, "en");
        Assert.Equal(3, _provider.Calls); // no unbounded retry loop

        // Recovery after the cooldown is possible without any manual step.
        await ExecAsync("UPDATE \"ListingTranslations\" SET \"RetryAfter\" = {0} WHERE \"PropertyId\" = {1}", DateTime.UtcNow.AddSeconds(-1), id);
        _provider.Behavior = null;
        Assert.Equal("ready", (await EnsureAsync(fast, id, "en"))!.Status);
    }

    [Fact(DisplayName = "A provider time-out returns promptly and the listing page is not blocked")]
    public async Task ProviderTimeout_ReturnsPromptly()
    {
        var id = await SeedAsync();
        using var impatient = Build(_provider, new() { ["ListingTranslation:ProviderTimeoutSeconds"] = "1" });
        _provider.Behavior = async (texts, src, target, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return FakeProvider.Echo(texts, src, target);
        };

        var started = DateTime.UtcNow;
        var dto = await EnsureAsync(impatient, id, "en");

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Equal("unavailable", dto!.Status);
        Assert.DoesNotContain(await RowsAsync(id), r => r.Status == ListingTranslationStatus.Completed);
    }

    [Fact(DisplayName = "Blank, oversized or control-character provider output is never stored as a success")]
    public async Task InvalidProviderOutput_IsRejected()
    {
        var id = await SeedAsync();
        foreach (var bad in new[] { "   ", new string('x', 5000), "ab\u0000cd" })
        {
            _provider.Behavior = (texts, _, _, _) => Task.FromResult(TranslationBatchResult.Success(
                texts.Select(_ => new TranslatedText(bad, "ar")).ToList()));
            using var f = Build(_provider, new() { ["ListingTranslation:ShortCooldownSeconds"] = "0", ["ListingTranslation:MaxAttempts"] = "99" });
            var dto = await EnsureAsync(f, id, "en");
            Assert.Equal("unavailable", dto!.Status);
            await ExecAsync("UPDATE \"ListingTranslations\" SET \"RetryAfter\" = now() - interval '1 minute' WHERE \"PropertyId\" = {0}", id);
        }
        Assert.DoesNotContain(await RowsAsync(id), r => r.Status == ListingTranslationStatus.Completed);
    }

    [Fact(DisplayName = "When the monthly character cap is reached no new translations are made, but stored ones keep being served")]
    public async Task MonthlyCap_StopsNewTranslations_ButServesStored()
    {
        var first = await SeedAsync();
        var second = await SeedAsync("شقة ثانية للبيع في الشعلان", "وصف الشقة الثانية في حي الشعلان بدمشق.");
        var chars = ArabicTitle.Length + ArabicDescription.Length;

        using var capped = Build(_provider, new() { ["ListingTranslation:MonthlyCharacterLimit"] = chars.ToString() });
        Assert.Equal("ready", (await EnsureAsync(capped, first, "en"))!.Status);

        var refused = await EnsureAsync(capped, second, "en");
        Assert.Equal("unavailable", refused!.Status);
        Assert.Equal("quota", refused.Reason);
        Assert.Equal(1, _provider.Calls);

        // Repeats during the quota cooldown do not even touch the budget table's counter again.
        for (var i = 0; i < 5; i++) await EnsureAsync(capped, second, "en");
        Assert.Equal(1, _provider.Calls);

        Assert.Equal("ready", (await EnsureAsync(capped, first, "en"))!.Status);
        await using var scope = _factory.Services.CreateAsyncScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranslationMonthlyUsages.SingleAsync();
        Assert.Equal(chars, usage.Characters);
    }

    [Fact(DisplayName = "Concurrent reservations never exceed the monthly cap")]
    public async Task BudgetReservation_IsAtomic()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(async _ =>
        {
            await using var s = _factory.Services.CreateAsyncScope();
            return await s.ServiceProvider.GetRequiredService<IListingTranslationStore>()
                .TryReserveBudgetAsync("2099-01", 100, 1_000, DateTime.UtcNow, CancellationToken.None);
        }));

        Assert.Equal(10, results.Count(r => r));
    }

    [Fact(DisplayName = "The creation kill-switch stops new translations but keeps serving stored ones; the master switch stops everything")]
    public async Task KillSwitches()
    {
        var id = await SeedAsync();
        var other = await SeedAsync("شقة ثالثة للإيجار في كفرسوسة", "وصف الشقة الثالثة في حي كفرسوسة بدمشق.");
        await EnsureAsync(id, "en");

        using var noCreate = Build(_provider, new() { ["ListingTranslation:CreationEnabled"] = "false" });
        Assert.Equal("ready", (await EnsureAsync(noCreate, id, "en"))!.Status);
        var blocked = await EnsureAsync(noCreate, other, "en");
        Assert.Equal("unavailable", blocked!.Status);
        Assert.Equal("disabled", blocked.Reason);
        Assert.Equal(1, _provider.Calls);

        using var off = Build(_provider, new() { ["ListingTranslation:Enabled"] = "false" });
        var disabled = await EnsureAsync(off, id, "en");
        Assert.Equal("unavailable", disabled!.Status);
        Assert.Null(disabled.Title.Text);
    }

    [Fact(DisplayName = "Without a configured provider nothing is claimed, stored or sent")]
    public async Task NoProvider_DoesNothing()
    {
        var id = await SeedAsync();
        using var none = Build(new FakeProvider { Available = false });
        var dto = await EnsureAsync(none, id, "en");

        Assert.Equal("unavailable", dto!.Status);
        Assert.Equal("disabled", dto.Reason);
        Assert.Empty(await RowsAsync(id));
    }

    [Fact(DisplayName = "Text that is too long, mixed-script, or not language is not translated automatically")]
    public async Task UnclassifiableOrOversizedText_IsLeftAlone()
    {
        var mixed = await SeedAsync("شقة apartment شقة apartment", "وصف description وصف description ok");
        var longText = await SeedAsync(ArabicTitle, new string('ب', 100));
        using var small = Build(_provider, new() { ["ListingTranslation:MaxFieldCharacters"] = "50" });

        var m = await EnsureAsync(mixed, "en");
        Assert.Equal("unavailable", m!.Title.State);
        Assert.Equal("unknown_source_language", m.Title.Reason);

        var l = await EnsureAsync(small, longText, "en");
        Assert.Equal("too_long", l!.Description.Reason);
        Assert.Equal("ready", l.Title.State);
    }

    // ── HTTP surface ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "POST /api/properties/{id}/translation: validates the language, hides unavailable listings, and is not cacheable")]
    public async Task Http_Contract()
    {
        var id = await SeedAsync();
        var hidden = await SeedAsync(published: false);
        using var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/properties/{id}/translation?lang=fr", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/properties/{id}/translation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/properties/{hidden}/translation?lang=en", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/properties/{Guid.NewGuid()}/translation?lang=en", null)).StatusCode);
        Assert.Equal(0, _provider.Calls);

        var response = await client.PostAsync($"/api/properties/{id}/translation?lang=EN-us", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", body.GetProperty("status").GetString());
        Assert.Equal("en", body.GetProperty("targetLanguage").GetString());
        Assert.Equal($"[en] {ArabicTitle}", body.GetProperty("title").GetProperty("text").GetString());

        // The listing itself loads independently of translation.
        var detail = await client.GetAsync($"/api/properties/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact(DisplayName = "The listing detail still loads when translation is broken")]
    public async Task ListingDetail_IsIndependentOfTranslation()
    {
        var id = await SeedAsync();
        _provider.Behavior = (_, _, _, _) => throw new InvalidOperationException("provider exploded");
        using var client = _factory.CreateClient();

        var translation = await client.PostAsync($"/api/properties/{id}/translation?lang=en", null);
        Assert.Equal(HttpStatusCode.OK, translation.StatusCode);
        var body = await translation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unavailable", body.GetProperty("status").GetString());

        var detail = await client.GetAsync($"/api/properties/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains(ArabicTitle, await detail.Content.ReadAsStringAsync());
    }

    private sealed class FakeProvider : ITranslationProvider
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public ConcurrentQueue<(IReadOnlyList<string> Texts, string? Source, string Target)> Log { get; } = new();
        public Func<IReadOnlyList<string>, string?, string, CancellationToken, Task<TranslationBatchResult>>? Behavior { get; set; }
        public bool Available { get; set; } = true;

        public string Name => "fake";
        public bool IsAvailable => Available;

        public static TranslationBatchResult Echo(IReadOnlyList<string> texts, string? source, string target) =>
            TranslationBatchResult.Success(texts.Select(t => new TranslatedText($"[{target}] {t}", source)).ToList());

        public Task<TranslationBatchResult> TranslateAsync(
            IReadOnlyList<string> texts, string? sourceLanguage, string targetLanguage, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            Log.Enqueue((texts, sourceLanguage, targetLanguage));
            return Behavior is null
                ? Task.FromResult(Echo(texts, sourceLanguage, targetLanguage))
                : Behavior(texts, sourceLanguage, targetLanguage, ct);
        }
    }
}
