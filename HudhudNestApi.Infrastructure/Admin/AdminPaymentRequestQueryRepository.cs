using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Domain.Plans.Enums;
using HudhudNestApi.Domain.Transactions.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Admin;

public sealed class AdminPaymentRequestQueryRepository : IAdminPaymentRequestQueryRepository
{
    // The queue is worked by hand; a cap keeps one response bounded if it ever backs up.
    private const int MaxRows = 200;

    private readonly AppDbContext _db;

    public AdminPaymentRequestQueryRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PendingPlanRequestDto>> GetPendingPlanRequestsAsync(
        CancellationToken ct = default)
    {
        var rows = await (
            from request in _db.PlanRequests.AsNoTracking()
            where request.Status == PlanRequestStatus.Pending
            join plan in _db.Plans.AsNoTracking() on request.PlanId equals plan.Id
            join account in _db.UserAccounts.AsNoTracking() on request.UserId equals account.Id
            join identity in _db.Users.AsNoTracking() on request.UserId equals identity.Id into identities
            from identity in identities.DefaultIfEmpty()
            orderby request.CreatedAt
            select new
            {
                request.Id,
                request.UserId,
                account.FirstName,
                account.LastName,
                account.DisplayName,
                Email = identity != null ? identity.Email : null,
                plan.Tier,
                plan.PriceUsd,
                request.CreatedAt
            })
            .Take(MaxRows)
            .ToListAsync(ct);

        return rows
            .Select(r => new PendingPlanRequestDto(
                r.Id,
                r.UserId,
                string.IsNullOrWhiteSpace(r.DisplayName) ? $"{r.FirstName} {r.LastName}".Trim() : r.DisplayName!,
                r.Email,
                r.Tier,
                r.PriceUsd,
                r.CreatedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<PendingFeaturedRequestDto>> GetPendingFeaturedRequestsAsync(
        CancellationToken ct = default)
    {
        var rows = await (
            from fee in _db.Transactions.AsNoTracking()
            where fee.TransactionType == TransactionType.FeaturedListingFee
                  && fee.Status == TransactionStatus.Pending
            join property in _db.Properties.AsNoTracking() on fee.PropertyId equals property.Id
            join account in _db.UserAccounts.AsNoTracking() on fee.PayerId equals account.Id
            join identity in _db.Users.AsNoTracking() on fee.PayerId equals identity.Id into identities
            from identity in identities.DefaultIfEmpty()
            orderby fee.TransactedAt
            select new
            {
                fee.Id,
                fee.PropertyId,
                property.Title,
                fee.PayerId,
                account.FirstName,
                account.LastName,
                account.DisplayName,
                Email = identity != null ? identity.Email : null,
                fee.AmountInUSD,
                fee.TransactedAt
            })
            .Take(MaxRows)
            .ToListAsync(ct);

        return rows
            .Select(r => new PendingFeaturedRequestDto(
                r.Id,
                r.PropertyId,
                r.Title,
                r.PayerId,
                string.IsNullOrWhiteSpace(r.DisplayName) ? $"{r.FirstName} {r.LastName}".Trim() : r.DisplayName!,
                r.Email,
                r.AmountInUSD,
                r.TransactedAt))
            .ToList();
    }
}
