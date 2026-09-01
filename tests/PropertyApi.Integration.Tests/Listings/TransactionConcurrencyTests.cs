using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Transactions.Entities;
using PropertyApi.Domain.Transactions.Enums;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Integration.Tests.Auth;
using Xunit;

namespace PropertyApi.Integration.Tests.Listings;

/// <summary>
/// Exercises Transaction's xmin concurrency token (RELEASE-BLOCKERS-AR.md B-9b) against a
/// real PostgreSQL server, mirroring PropertyConcurrencyTests — see that file's header for
/// why this needs Postgres rather than the InMemory provider.
///
/// The scenario is the race ConfirmFeaturedListingPaymentCommandHandler /
/// ConfirmListingExtensionPaymentCommandHandler are exposed to: both read a fee, check
/// Status == Pending, then mark it Completed and grant the paid effect. Two callers racing
/// the same fee (double-submit, two admins) could previously both pass the Pending check
/// before either write landed.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class TransactionConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName =
        "Confirming a Transaction loaded before another writer's update throws DbUpdateConcurrencyException")]
    public async Task SaveChanges_OnAStaleRow_ThrowsConcurrencyException_InsteadOfSilentlyOverwriting()
    {
        var transactionId = await SeedPendingFeeAsync();

        // Two independent contexts loading the same fee — the shape of two concurrent
        // confirmation attempts (a double-submit, or two administrators).
        await using var scopeA = _factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var feeA = await dbA.Set<Transaction>().SingleAsync(t => t.Id == transactionId);

        await using var scopeB = _factory.Services.CreateAsyncScope();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        var feeB = await dbB.Set<Transaction>().SingleAsync(t => t.Id == transactionId);

        feeA.MarkCompleted();
        await dbA.SaveChangesAsync();

        // B's copy still carries the xmin value the row had before A's write. Without the
        // concurrency token this second MarkCompleted + SaveChanges would succeed too,
        // silently granting the paid effect a second time for one payment.
        feeB.MarkCompleted();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => dbB.SaveChangesAsync());
    }

    [Fact(DisplayName = "Confirming a Transaction nobody else touched succeeds normally")]
    public async Task SaveChanges_WithNoConcurrentWriter_Succeeds()
    {
        var transactionId = await SeedPendingFeeAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fee = await db.Set<Transaction>().SingleAsync(t => t.Id == transactionId);

        fee.MarkCompleted();

        // The xmin token must not turn every ordinary confirmation into a conflict — only a
        // save that raced another writer should.
        await db.SaveChangesAsync();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await verifyDb.Set<Transaction>().SingleAsync(t => t.Id == transactionId);

        Assert.Equal(TransactionStatus.Completed, reloaded.Status);
    }

    private async Task<Guid> SeedPendingFeeAsync()
    {
        // BUG-27: Transaction.PropertyId/PayerId/ReceiverId each carry a real FK
        // (FK_Transactions_Properties_PropertyId, and two FKs to UserAccounts) — random
        // Guids with no matching rows violate those constraints on real Postgres, same
        // class of bug as PropertyConcurrencyTests/UserAccountConcurrencyTests.
        var payer = await _factory.SeedUserAsync($"transaction-payer-{Guid.NewGuid():N}@test.local");
        var receiver = await _factory.SeedUserAsync($"transaction-receiver-{Guid.NewGuid():N}@test.local");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var property = Property.Create(
            "شقة للإيجار",
            "وصف كافٍ للإعلان",
            payer.UserAccountId,
            ListingType.ForRent);
        db.Properties.Add(property);

        var fee = Transaction.Create(
            propertyId: property.Id,
            payerId: payer.UserAccountId,
            receiverId: receiver.UserAccountId,
            transactionType: TransactionType.FeaturedListingFee,
            amount: 5m,
            currencyId: 1,
            exchangeRateToUSD: 1m,
            paymentMethod: TransactionPaymentMethod.Cash);

        db.Set<Transaction>().Add(fee);
        await db.SaveChangesAsync();

        return fee.Id;
    }
}
