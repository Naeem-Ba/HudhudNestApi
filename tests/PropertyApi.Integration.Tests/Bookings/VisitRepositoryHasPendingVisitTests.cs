using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Domain.Bookings.Entities;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Bookings;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Integration.Tests.Auth;
using Xunit;

namespace PropertyApi.Integration.Tests.Bookings;

/// <summary>
/// Exercises VisitRepository.HasPendingVisitAsync against a real PostgreSQL server —
/// mirrors PropertyConcurrencyTests/UserAccountConcurrencyTests' use of
/// PostgresAuthTestFactory rather than building a parallel host.
///
/// fix/visit-request-notification-actions changed this query's predicate from
/// "Status == Pending" to "Status == Pending || Status == RescheduleProposed" so a
/// requester awaiting the owner's counter-proposed time cannot open a second,
/// duplicate visit request for the same property in the meantime. That predicate is a
/// LINQ expression translated by EF Core to SQL — the InMemory provider used by the
/// rest of this assembly's fast tests would happily evaluate it in .NET regardless of
/// whether the translation is correct, so only a real server actually proves it.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Bookings")]
public sealed class VisitRepositoryHasPendingVisitTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Theory(DisplayName = "HasPendingVisitAsync is true for Pending and RescheduleProposed, false otherwise")]
    [InlineData(0, true)]   // Pending
    [InlineData(5, true)]   // RescheduleProposed
    [InlineData(1, false)]  // Confirmed
    [InlineData(2, false)]  // Declined
    [InlineData(3, false)]  // Cancelled
    public async Task HasPendingVisitAsync_ReflectsWhetherTheRequesterIsAwaitingAResponse(
        int visitStatus, bool expectedPending)
    {
        var owner = await _factory.SeedUserAsync($"visit-owner-{Guid.NewGuid():N}@test.local");
        var requester = await _factory.SeedUserAsync($"visit-requester-{Guid.NewGuid():N}@test.local");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var property = Property.Create(
            "شقة للإيجار", "وصف كافٍ للإعلان", owner.UserAccountId, ListingType.ForRent);
        db.Properties.Add(property);

        var visit = VisitRequest.Create(
            property.Id, requester.UserAccountId, DateTime.UtcNow.AddDays(1), "زائر", "0999999999");
        DriveToStatus(visit, (PropertyApi.Domain.Bookings.Enums.VisitStatus)visitStatus);
        db.VisitRequests.Add(visit);

        await db.SaveChangesAsync();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = new VisitRepository(verifyDb);

        var isPending = await repository.HasPendingVisitAsync(
            property.Id, requester.UserAccountId, CancellationToken.None);

        Assert.Equal(expectedPending, isPending);
    }

    /// <summary>Drives a fresh Pending visit to <paramref name="status"/> via the real
    /// domain state-machine methods only, so every seeded row is itself a state the
    /// domain actually allows into that status.</summary>
    private static void DriveToStatus(
        VisitRequest visit, PropertyApi.Domain.Bookings.Enums.VisitStatus status)
    {
        switch (status)
        {
            case PropertyApi.Domain.Bookings.Enums.VisitStatus.Pending:
                break;
            case PropertyApi.Domain.Bookings.Enums.VisitStatus.Confirmed:
                visit.Confirm();
                break;
            case PropertyApi.Domain.Bookings.Enums.VisitStatus.Declined:
                visit.Decline();
                break;
            case PropertyApi.Domain.Bookings.Enums.VisitStatus.Cancelled:
                visit.Cancel(visit.RequesterId);
                break;
            case PropertyApi.Domain.Bookings.Enums.VisitStatus.RescheduleProposed:
                visit.ProposeAlternate(DateTime.UtcNow.AddDays(3));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }
}
