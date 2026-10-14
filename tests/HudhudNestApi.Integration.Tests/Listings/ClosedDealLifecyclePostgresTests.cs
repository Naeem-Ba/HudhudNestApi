using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Listings;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// The Sold/Rented lifecycle (QA Phase 7, P7-D08): the expiry sweep leaves a concluded deal alone,
/// and its photos are compressed once, after the reversal window, by the provider-agnostic compactor
/// (a recording fake here; the Cloudinary implementation cannot be reached from the build environment).
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class ClosedDealLifecyclePostgresTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _baseFactory = new();
    private readonly RecordingCompactor _compactor = new();
    private readonly string _marker = $"cd{Guid.NewGuid():N}";
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory = null!;
    private Guid _ownerId;

    public async Task InitializeAsync()
    {
        await _baseFactory.PrepareDatabaseAsync();
        _ownerId = (await _baseFactory.SeedUserAsync($"{_marker}@test.local")).UserAccountId;
        _factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IListingImageCompactor>();
            services.AddSingleton<IListingImageCompactor>(_compactor);
        }));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        _baseFactory.Dispose();
        return Task.CompletedTask;
    }

    private async Task SweepAsync()
    {
        var service = new ListingExpiryHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<ListingExpiryHostedService>>());
        await service.RunOnceAsync(CancellationToken.None);
    }

    private async Task<Guid> SeedAsync(
        string name, PropertyStatus status, DateTime? expiresAt = null, DateTime? closedAt = null, int images = 0)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var property = Property.Create($"{_marker} {name}", "وصف كافٍ للإعلان", _ownerId, ListingType.ForSale);
        property.ExpiresAt = expiresAt;
        property.ChangeStatus(status);
        for (var i = 0; i < images; i++)
            property.Images.Add(new PropertyImage { Url = $"https://cdn.test/{name}-{i}.jpg", PublicId = $"{_marker}/{name}-{i}", IsMain = i == 0, SortOrder = i });
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        if (closedAt is { } closed)
        {
            db.Entry(property).Property(p => p.ClosedAt).CurrentValue = closed;
            await db.SaveChangesAsync();
        }

        return property.Id;
    }

    private async Task<Property> LoadAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Properties.IgnoreQueryFilters().Include(p => p.Images).AsNoTracking().SingleAsync(p => p.Id == id);
    }

    [Fact(DisplayName = "A sold or rented listing past its publication window is neither expired nor deleted by the sweep")]
    public async Task Sweep_LeavesClosedDealsAlone()
    {
        var past = DateTime.UtcNow.AddDays(-400);   // beyond window and grace
        var sold = await SeedAsync("sold", PropertyStatus.Sold, expiresAt: past);
        var rented = await SeedAsync("rented", PropertyStatus.Rented, expiresAt: past);
        var available = await SeedAsync("available", PropertyStatus.Available, expiresAt: DateTime.UtcNow.AddDays(-1));

        await SweepAsync();

        foreach (var id in new[] { sold, rented })
        {
            var row = await LoadAsync(id);
            Assert.True(row.IsPublished);
            Assert.False(row.IsDeleted);
            Assert.NotEqual(PropertyStatus.Expired, row.Status);
        }

        Assert.Equal(PropertyStatus.Expired, (await LoadAsync(available)).Status);
    }

    [Fact(DisplayName = "Photos of a deal closed longer than the reversal window are compressed once, and the URL the provider returns is stored")]
    public async Task Sweep_CompactsOldClosedDeals_Once()
    {
        var id = await SeedAsync("old-sold", PropertyStatus.Sold, closedAt: DateTime.UtcNow.AddDays(-15), images: 2);

        await SweepAsync();

        var row = await LoadAsync(id);
        Assert.NotNull(row.ImagesCompactedAt);
        Assert.Equal(2, _compactor.Calls.Count(c => c.PublicId.StartsWith(_marker, StringComparison.Ordinal)));
        Assert.All(row.Images, image => Assert.EndsWith("?compact=1", image.Url));

        // A second sweep does not touch them again.
        _compactor.Calls.Clear();
        await SweepAsync();
        Assert.Empty(_compactor.Calls);
    }

    [Fact(DisplayName = "Deals closed inside the window, reopened deals and market listings keep their original photos")]
    public async Task Sweep_DoesNotCompactRecentOrReopenedListings()
    {
        var recent = await SeedAsync("recent-sold", PropertyStatus.Sold, closedAt: DateTime.UtcNow.AddDays(-3), images: 1);
        var reopened = await SeedAsync("reopened", PropertyStatus.Sold, closedAt: DateTime.UtcNow.AddDays(-30), images: 1);
        var live = await SeedAsync("live", PropertyStatus.Available, images: 1);

        // The buyer backed out: the owner returns it to Available, which clears the closing date.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var property = await db.Properties.SingleAsync(p => p.Id == reopened);
            property.ChangeStatus(PropertyStatus.Available);
            await db.SaveChangesAsync();
        }

        await SweepAsync();

        Assert.Null((await LoadAsync(recent)).ImagesCompactedAt);
        Assert.Null((await LoadAsync(reopened)).ImagesCompactedAt);
        Assert.Null((await LoadAsync(live)).ImagesCompactedAt);
        Assert.DoesNotContain(_compactor.Calls, c => c.PublicId.StartsWith($"{_marker}/recent", StringComparison.Ordinal));
        Assert.DoesNotContain(_compactor.Calls, c => c.PublicId.StartsWith($"{_marker}/reopened", StringComparison.Ordinal));
        Assert.DoesNotContain(_compactor.Calls, c => c.PublicId.StartsWith($"{_marker}/live", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "A provider failure leaves the listing unstamped so a later sweep retries it")]
    public async Task Sweep_RetriesAfterProviderFailure()
    {
        var id = await SeedAsync("flaky", PropertyStatus.Rented, closedAt: DateTime.UtcNow.AddDays(-20), images: 2);
        _compactor.FailPublicIds.Add($"{_marker}/flaky-1");

        await SweepAsync();
        Assert.Null((await LoadAsync(id)).ImagesCompactedAt);

        _compactor.FailPublicIds.Clear();
        await SweepAsync();
        Assert.NotNull((await LoadAsync(id)).ImagesCompactedAt);
    }

    [Fact(DisplayName = "The default request logging does not record the visitor's exact coordinates (P7-D04)")]
    public async Task RequestLogging_DoesNotRecordCoordinates()
    {
        const string coordinates = "latitude=33.51234&longitude=36.27654";

        var captured = await CaptureLogsForGeoSearchAsync(coordinates, configure: null);
        Assert.DoesNotContain(captured, line => line.Contains("33.51234") || line.Contains("36.27654"));

        // Control: with the shipped setting removed the same request does log them. Without this the
        // assertion above could pass only because nothing is captured at all.
        var control = await CaptureLogsForGeoSearchAsync(
            coordinates, configure: c => c["Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information");
        Assert.Contains(control, line => line.Contains("33.51234"));
    }

    private async Task<List<string>> CaptureLogsForGeoSearchAsync(string query, Action<IDictionary<string, string?>>? configure)
    {
        var sink = new CapturingLoggerProvider();
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(sink));
            if (configure is not null)
            {
                var overrides = new Dictionary<string, string?>();
                configure(overrides);
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(overrides));
            }
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/properties/geo-search?{query}&radiusKm=2");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return sink.Messages.ToList();
    }

    private sealed class RecordingCompactor : IListingImageCompactor
    {
        public List<(string PublicId, string Url)> Calls { get; } = new();
        public HashSet<string> FailPublicIds { get; } = new();

        public Task<ImageCompactionResult> CompactAsync(string publicId, string currentUrl, CancellationToken cancellationToken = default)
        {
            lock (Calls) Calls.Add((publicId, currentUrl));
            return Task.FromResult(FailPublicIds.Contains(publicId)
                ? ImageCompactionResult.Failure("simulated provider failure")
                : ImageCompactionResult.Success(currentUrl + "?compact=1"));
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Messages);

        public void Dispose() { }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => sink.Enqueue($"{category}: {formatter(state, exception)}");
        }
    }
}
