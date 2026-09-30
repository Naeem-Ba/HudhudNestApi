using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Listings.Commands.CreateProperty;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Integration.Tests.SocialDistribution;

/// <summary>
/// Regression coverage for the Mandatory Staging Deploy + E2E Smoke "Cleanup expected HTTP 200,
/// got 500" failure (run 36350590745, commit 4fdaf362809c8e77ff9c6010611239c855cb4782):
/// PropertyPublishedDistributionHandler runs DistributionEngine.RunAsync on every publish and
/// persists a DistributionRun row for that property unconditionally -- even with zero matching
/// rules/accounts (DistributionRun's PropertyId FK is Restrict) -- but
/// StagingTestSupportController.Cleanup never deleted DistributionRuns (or the rest of the
/// SocialDistribution audit trail) before hard-deleting the property, so every Staging smoke run
/// that publishes a property leaves an FK violation behind for the next cleanup call.
/// </summary>
public sealed class StagingCleanupDistributionRunFkTests
    : IClassFixture<SocialDistributionApiTestFactory>, IAsyncLifetime
{
    private readonly SocialDistributionApiTestFactory _factory;

    public StagingCleanupDistributionRunFkTests(SocialDistributionApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> CreatePublishedPropertyAsync(AppDbContext db, ISender sender, Guid ownerId)
    {
        var governorate = await db.Governorates.OrderBy(g => g.Id).FirstAsync();
        var propertyType = await db.PropertyTypes.OrderBy(t => t.Id).FirstAsync();

        return await sender.Send(new CreatePropertyCommand(
            ownerId, "فيلا للإيجار في دمشق", "وصف كامل وتفصيلي لهذا العقار يشرح مساحته وموقعه بدقة",
            ListingType.ForRent, "شارع الثورة", "دمشق", null, "SY", null,
            governorate.Id, null, "المزة", null, null, propertyType.Id, null, null,
            500m, null, null, null, null, "USD", 4, 200m, 2, true, true, false,
            HeatingType.Central, null, null,
            LegalStatus: null,
            RentalStartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            RentalEndDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            RentalDurationType: RentalDurationType.OneYear));
    }

    [Fact]
    public async Task Publishing_a_property_leaves_a_distribution_run_even_with_no_rules_configured()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var owner = await _factory.SeedUserAsync("cleanup-owner");

        var propertyId = await CreatePublishedPropertyAsync(db, sender, owner.Id);

        // No channel/account/rule was ever configured in this test -- proves the run is created
        // unconditionally by DistributionEngine.RunAsync, not only when a rule actually matches.
        Assert.True(await db.DistributionRuns.AnyAsync(r => r.PropertyId == propertyId));
    }

    [Fact]
    public async Task Hard_deleting_the_property_without_clearing_its_distribution_run_violates_the_restrict_fk()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var owner = await _factory.SeedUserAsync("cleanup-owner-old-order");

        var propertyId = await CreatePublishedPropertyAsync(db, sender, owner.Id);
        Assert.True(await db.DistributionRuns.AnyAsync(r => r.PropertyId == propertyId));

        // This is exactly what StagingTestSupportController.Cleanup did before the fix: delete the
        // property with no prior SocialDistribution cleanup. Reproduces the real HTTP 500 against
        // an actual PostgreSQL Restrict FK, not a hypothesis.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Properties.IgnoreQueryFilters().Where(p => p.Id == propertyId).ExecuteDeleteAsync());
    }

    [Fact]
    public async Task Cleanup_ordering_deletes_the_distribution_run_before_the_property_succeeds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var owner = await _factory.SeedUserAsync("cleanup-owner-fixed-order");

        var propertyId = await CreatePublishedPropertyAsync(db, sender, owner.Id);
        Assert.True(await db.DistributionRuns.AnyAsync(r => r.PropertyId == propertyId));

        // The exact ordering StagingTestSupportController.Cleanup now performs (deepest
        // SocialDistribution dependents first, Properties last).
        var socialPublicationIds = await db.SocialPublications
            .Where(x => x.PropertyId == propertyId)
            .Select(x => x.Id)
            .ToListAsync();
        await db.SocialPublicationDeadLetters
            .Where(x => socialPublicationIds.Contains(x.PublicationId))
            .ExecuteDeleteAsync();
        await db.SocialPublicationStatusHistories
            .Where(x => socialPublicationIds.Contains(x.SocialPublicationId))
            .ExecuteDeleteAsync();
        await db.SocialMediaAssets
            .Where(x => x.PropertyId == propertyId)
            .ExecuteDeleteAsync();
        await db.SocialPublications
            .Where(x => x.PropertyId == propertyId)
            .ExecuteDeleteAsync();
        await db.DistributionRuns
            .Where(x => x.PropertyId == propertyId)
            .ExecuteDeleteAsync();

        await db.Properties.IgnoreQueryFilters().Where(p => p.Id == propertyId).ExecuteDeleteAsync();

        Assert.False(await db.Properties.IgnoreQueryFilters().AnyAsync(p => p.Id == propertyId));
        Assert.False(await db.DistributionRuns.AnyAsync(r => r.PropertyId == propertyId));
    }
}
