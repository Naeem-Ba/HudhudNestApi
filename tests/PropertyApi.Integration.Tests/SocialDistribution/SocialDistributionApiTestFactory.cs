using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Models;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.Persistence.Seeds;
using PropertyApi.Infrastructure.SocialDistribution;
using PropertyApi.Infrastructure.SocialDistribution.Publishing;

namespace PropertyApi.Integration.Tests.SocialDistribution;

/// <summary>
/// Real-pipeline fixture for the automatic social distribution engine: boots the actual
/// <c>Program</c> host against PostgreSQL (real MediatR, EF Core, [Authorize], the real
/// dispatch worker), and fakes only the two things that would leave the machine — Cloudinary
/// (<see cref="RecordingMediaStorage"/>) and the platform APIs (<see cref="ScriptedSocialPublisher"/>).
///
/// The existing unit tests exercise each handler against Mocks and were all green while the
/// property-created trigger and the Facebook/Telegram body were broken in the real system; this
/// fixture is what proves the whole chain: listing created → engine → queue → worker → publisher.
/// </summary>
public sealed class SocialDistributionApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        ?? throw new InvalidOperationException(
            "A PostgreSQL test connection string is required. Set TEST_POSTGRES_CONNECTION_STRING " +
            "or ConnectionStrings__DefaultConnection.");

    public RecordingMediaStorage Storage { get; } = new();

    public IReadOnlyDictionary<SocialPlatform, ScriptedSocialPublisher> Publishers { get; } =
        new Dictionary<SocialPlatform, ScriptedSocialPublisher>
        {
            [SocialPlatform.Facebook] = new(new FacebookPublisher(NullLogger<FacebookPublisher>.Instance)),
            [SocialPlatform.Instagram] = new(new InstagramPublisher(NullLogger<InstagramPublisher>.Instance)),
            [SocialPlatform.Telegram] = new(new TelegramPublisher(NullLogger<TelegramPublisher>.Instance)),
        };

    /// <summary>Extra configuration a test wants layered on top of the defaults (set BEFORE the first CreateClient/Services access).</summary>
    public Dictionary<string, string?> ConfigurationOverrides { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["RateLimiting:Redis:Enabled"] = "false",
                ["DataProtection:PersistKeysToDatabase"] = "true",
                ["OtpSettings:SecretKey"] = "integration-test-otp-secret-key-at-least-32-bytes",
                ["Security:PhoneLookupHmacKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
                ["Frontend:BaseUrl"] = "https://realestateworld.world",
            };

            foreach (var pair in ConfigurationOverrides)
                settings[pair.Key] = pair.Value;

            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());

            services.RemoveAll<IMediaStorageService>();
            services.AddSingleton<IMediaStorageService>(Storage);

            // Registered AFTER the placeholders: SocialPublisherRegistry lets the last
            // registration for a platform win — exactly how a real publisher will be swapped in.
            foreach (var publisher in Publishers.Values)
                services.AddSingleton<ISocialPublisher>(publisher);

            // The real worker loops must not race a test that drives RunOnceAsync directly — kept
            // resolvable (not removed outright) so ExpiryWorker below still works; every other
            // unrelated sweep (SavedSearchMatch, audit retention, ...) is dropped since nothing
            // here exercises them.
            services.RemoveAll<IHostedService>();
            services.AddSingleton<IHostedService, SocialPublicationDispatchHostedService>();
            services.AddSingleton<IHostedService, PropertyApi.Infrastructure.Listings.ListingExpiryHostedService>();
        });
    }

    /// <summary>The dispatch worker instance the host would run — tests call its internal single-sweep entry point.</summary>
    public SocialPublicationDispatchHostedService Worker =>
        Services.GetServices<IHostedService>().OfType<SocialPublicationDispatchHostedService>().Single();

    /// <summary>The listing-expiry sweep instance the host would run — tests call its internal single-sweep entry point.</summary>
    public PropertyApi.Infrastructure.Listings.ListingExpiryHostedService ExpiryWorker =>
        Services.GetServices<IHostedService>().OfType<PropertyApi.Infrastructure.Listings.ListingExpiryHostedService>().Single();

    /// <summary>One full worker sweep: reconciliation, then dispatch of everything due.</summary>
    public Task RunWorkerSweepAsync() => Worker.RunOnceAsync(CancellationToken.None);

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(scope.ServiceProvider);

        // Properties CASCADE also clears everything that references a listing (runs, publications,
        // images, favourites...) so the reconciliation sweep sees only this test's listings.
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"SocialPublicationDeadLetters\", \"SocialPublicationStatusHistories\", \"SocialPublications\", " +
            "\"SocialPostContents\", \"DistributionRules\", \"DistributionRuns\", \"SocialAccounts\", " +
            "\"SocialChannels\", \"SocialMediaAssets\", \"Properties\" RESTART IDENTITY CASCADE;");

        Storage.Uploads.Clear();
        foreach (var publisher in Publishers.Values)
            publisher.Reset();
    }

    public async Task<SeededSocialUser> SeedUserAsync(string emailPrefix, params string[] roles)
    {
        using var scope = Services.CreateScope();
        var registration = scope.ServiceProvider.GetRequiredService<IRegisterIdentityService>();
        var identityRead = scope.ServiceProvider.GetRequiredService<IUserIdentityReadService>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"{emailPrefix}.{Guid.NewGuid():N}@social-distribution-tests.local";
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var created = await registration.CreateAsync(
            new CreateIdentityAccount(id, email, null, "StrongPass!123", "Test", "User", now, true, null, false),
            CancellationToken.None);
        if (!created.Succeeded)
            throw new InvalidOperationException("Failed to seed integration-test identity.");

        // A listing needs a chosen plan (LISTING_PLAN_REQUIRED) — the highest-limit seeded plan,
        // so quota never gets in this fixture's way.
        var account = UserAccount.Create(id, "Test", "User", now);
        var plan = await db.Plans
            .OrderBy(p => p.ListingLimit == null ? 0 : 1)
            .ThenByDescending(p => p.ListingLimit)
            .FirstAsync();
        account.SelectPlan(plan.Id, now);
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        foreach (var role in roles)
        {
            if (!(await registration.AddToRoleAsync(id, role, CancellationToken.None)).Succeeded)
                throw new InvalidOperationException($"Failed to add role '{role}' to seeded test identity.");
        }

        var snapshot = await identityRead.FindByIdAsync(id, CancellationToken.None)
            ?? throw new InvalidOperationException("Seeded integration-test identity could not be reloaded.");

        return new SeededSocialUser(id, email, tokens.GenerateAccessToken(
            new AccessTokenSubject(id, email, email, snapshot.SecurityStamp), roles));
    }

    public HttpClient AuthedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Admin setup through the real HTTP API (channel → activate → account → connect → match-all
    /// rule) — what an operator does today via Swagger. Returns the account id.
    /// </summary>
    public async Task<Guid> ConfigurePlatformAsync(HttpClient admin, SocialPlatform platform, int? provinceId = null)
    {
        var channel = await SendAsync(admin.PostAsJsonAsync("/api/social-distribution/channels",
            new { platform = platform.ToString(), name = $"{platform} channel", configurationVersion = "v1" }));
        var channelId = channel.GetProperty("id").GetGuid();
        await SendAsync(admin.PostAsync($"/api/social-distribution/channels/{channelId}/activate", null));

        var account = await SendAsync(admin.PostAsJsonAsync("/api/social-distribution/accounts",
            new { socialChannelId = channelId, displayName = $"{platform} page", externalAccountId = $"ext-{platform}-{Guid.NewGuid():N}", accountType = "Page", governorateId = (int?)null }));
        var accountId = account.GetProperty("id").GetGuid();
        await SendAsync(admin.PostAsJsonAsync($"/api/social-distribution/accounts/{accountId}/connect",
            new { credentialReference = "test-credential-reference" }));

        await SendAsync(admin.PostAsJsonAsync("/api/social-distribution/rules", new
        {
            name = $"{platform} everything",
            description = "integration test",
            provinceId,
            propertyTypeId = (int?)null,
            transactionType = (string?)null,
            socialAccountId = accountId,
            priority = 10,
            startAt = (DateTime?)null,
            endAt = (DateTime?)null,
        }));

        return accountId;
    }

    /// <summary>All publications the admin API reports for one listing.</summary>
    public async Task<List<JsonElement>> GetPublicationsAsync(HttpClient admin, Guid propertyId)
    {
        var page = await SendAsync(admin.GetAsync("/api/social-distribution/publications?pageSize=100"));
        return page.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("propertyId").GetGuid() == propertyId)
            .ToList();
    }

    public static async Task<JsonElement> SendAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri?.AbsolutePath}: {body}");

        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Persists a published, public listing directly (no MediatR command, so NO PropertyPublishedEvent) — the "path that raises no event" the sweep exists for.</summary>
    public async Task<Guid> SeedPublishedPropertyAsync(
        Guid ownerId,
        ListingType listingType = ListingType.ForRent,
        DateTime? publishedAtUtc = null,
        int imageCount = 2,
        decimal? price = 500m,
        string title = "فيلا للإيجار")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Long enough to clear SocialDistributionEligibilityOptions' default MinDescriptionLength (20).
        var property = Property.Create(title, "وصف كامل وتفصيلي لهذا العقار يشرح مساحته وموقعه بدقة", ownerId, listingType, currencyCode: "USD", isPublished: true);
        property.GovernorateId = (await db.Governorates.OrderBy(g => g.Id).FirstAsync()).Id;
        property.PropertyTypeId = (await db.PropertyTypes.OrderBy(t => t.Id).FirstAsync()).Id;
        property.City = "دمشق";
        property.Area = 200;
        property.Rooms = 4;
        if (listingType == ListingType.ForSale) property.PurchasePrice = price; else property.ColdRent = price;
        for (var i = 0; i < imageCount; i++)
            property.Images.Add(new PropertyImage { Url = $"https://cdn.example.test/p{i}.jpg", PublicId = $"p{i}", IsMain = i == 0, SortOrder = i });

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        if (publishedAtUtc is { } publishedAt)
        {
            // PublishedAt has a private setter — backdating is only possible in SQL.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Properties\" SET \"PublishedAt\" = {publishedAt} WHERE \"Id\" = {property.Id}");
        }

        return property.Id;
    }
}

public sealed record SeededSocialUser(Guid Id, string Email, string AccessToken);

/// <summary>Cloudinary stand-in that records what would have been uploaded.</summary>
public sealed class RecordingMediaStorage : IMediaStorageService
{
    public List<(string FileName, string ContentType, byte[] Content, string Folder)> Uploads { get; } = new();

    public Task<MediaUploadResult> UploadImageAsync(Stream content, string fileName, string contentType, string folder, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        Uploads.Add((fileName, contentType, buffer.ToArray(), folder));
        return Task.FromResult(MediaUploadResult.Success($"https://cdn.example.test/{folder}/{fileName}", $"{folder}/{fileName}"));
    }

    public Task DeleteImageAsync(string publicId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<MediaFileResult?> GetImageAsync(string imageUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<MediaFileResult?>(null);
}

/// <summary>
/// Platform stand-in: real capabilities and real content validation (delegated to the shipped
/// placeholder for that platform), scripted publish outcome, full call recording. What a real
/// Telegram/Facebook/Instagram adapter must satisfy is the same <see cref="ISocialPublisher"/>
/// contract this fake is written against.
/// </summary>
public sealed class ScriptedSocialPublisher : ISocialPublisher
{
    private readonly ISocialPublisher _inner;
    private int _sequence;

    public ScriptedSocialPublisher(ISocialPublisher inner)
    {
        _inner = inner;
        OnPublish = DefaultPublish;
    }

    public SocialPlatform Platform => _inner.Platform;

    public Func<SocialPublishRequest, SocialPublishResult> OnPublish { get; set; }

    public List<SocialPublishRequest> Published { get; } = new();

    public List<(string Action, string ExternalPostId)> LifecycleCalls { get; } = new();

    public void Reset()
    {
        OnPublish = DefaultPublish;
        Published.Clear();
        LifecycleCalls.Clear();
        _sequence = 0;
    }

    private SocialPublishResult DefaultPublish(SocialPublishRequest request) =>
        SocialPublishResult.Success($"ext-{Platform}-{++_sequence}", $"https://social.example.test/{Platform}/{_sequence}");

    public SocialPublisherCapabilities GetCapabilities() => _inner.GetCapabilities();

    public SocialContentValidationResult ValidateContent(SocialPublishRequest request) => _inner.ValidateContent(request);

    public Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default)
    {
        Published.Add(request);
        return Task.FromResult(OnPublish(request));
    }

    public Task<SocialPublishResult> UpdateAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default)
    {
        LifecycleCalls.Add(("update", externalPostId));
        return Task.FromResult(SocialPublishResult.Success(externalPostId));
    }

    public Task<SocialPublishResult> CommentAsync(string externalPostId, string commentBody, CancellationToken ct = default)
    {
        LifecycleCalls.Add(("comment", externalPostId));
        return Task.FromResult(SocialPublishResult.Success(externalPostId));
    }

    public Task<SocialPublishResult> DeleteAsync(string externalPostId, CancellationToken ct = default)
    {
        LifecycleCalls.Add(("delete", externalPostId));
        return Task.FromResult(SocialPublishResult.Success(externalPostId));
    }
}
