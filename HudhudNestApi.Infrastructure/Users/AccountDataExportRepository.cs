using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Users;

/// <summary>
/// Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md). Every query here is scoped to
/// exactly <paramref name="userId"/> (or, for RatingsReceived, filtered to ratings *about*
/// that same id) -- there is no path in this class that can return another user's data, which
/// is the property that makes the endpoint IDOR-safe together with the controller deriving
/// userId only from the authenticated principal (see UsersController.ExportMyData).
/// </summary>
public sealed class AccountDataExportRepository : IAccountDataExportRepository
{
    private readonly AppDbContext _db;
    private readonly IUserIdentityReadService _identity;

    public AccountDataExportRepository(
        AppDbContext db,
        IUserIdentityReadService identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task<AccountDataExportDto> BuildExportAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var account = await _db.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == userId, ct)
            ?? throw new InvalidOperationException(
                $"UserAccount {userId} not found while building a data export.");

        var identity = await _identity.FindByIdAsync(userId, ct);
        var roles = await _identity.GetRolesAsync(userId, ct);

        var planTier = account.PlanId is { } planId
            ? await _db.Plans.AsNoTracking()
                .Where(p => p.Id == planId)
                .Select(p => p.Tier)
                .SingleOrDefaultAsync(ct)
            : null;

        var profile = new AccountDataExportProfile
        {
            Id = account.Id,
            Email = identity?.Email,
            PhoneNumber = identity?.PhoneNumber,
            FirstName = account.FirstName,
            LastName = account.LastName,
            DisplayName = account.DisplayName,
            Bio = account.Bio,
            ContactInfo = account.ContactInfo,
            PreferredLanguage = account.PreferredLanguage,
            PreferredCurrency = account.PreferredCurrency,
            CountryCode = account.CountryCode,
            PlanTier = planTier,
            Roles = roles,
            CreatedAt = account.CreatedAt
        };

        var properties = await _db.Properties
            .AsNoTracking()
            .Where(p => p.OwnerId == userId)
            .Select(p => new AccountDataExportProperty
            {
                Id = p.Id,
                Title = p.Title,
                Status = p.Status.ToString(),
                IsPublished = p.IsPublished,
                CreatedAt = p.CreatedAt,
                PublishedAt = p.PublishedAt
            })
            .ToListAsync(ct);

        var reviews = await _db.PropertyReviews
            .AsNoTracking()
            .Where(r => r.ReviewerId == userId)
            .Select(r => new AccountDataExportReview
            {
                PropertyId = r.PropertyId,
                PropertyTitle = r.Property != null ? r.Property.Title : string.Empty,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync(ct);

        var favorites = await _db.Favorites
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .Select(f => new AccountDataExportFavorite
            {
                PropertyId = f.PropertyId,
                PropertyTitle = f.Property != null ? f.Property.Title : string.Empty,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync(ct);

        var visits = await _db.VisitRequests
            .AsNoTracking()
            .Where(v => v.RequesterId == userId)
            .Select(v => new AccountDataExportVisitRequest
            {
                PropertyId = v.PropertyId,
                PropertyTitle = v.Property != null ? v.Property.Title : string.Empty,
                Status = v.Status.ToString(),
                ProposedAt = v.ProposedAt,
                CreatedAt = v.CreatedAt
            })
            .ToListAsync(ct);

        var consents = await _db.Set<HudhudNestApi.Domain.Users.Entities.ConsentRecord>()
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new AccountDataExportConsent
            {
                PolicyType = c.PolicyType.ToString(),
                PolicyVersion = c.PolicyVersion,
                Source = c.Source.ToString(),
                ConsentedAtUtc = c.ConsentedAtUtc,
                WithdrawnAtUtc = c.WithdrawnAtUtc
            })
            .ToListAsync(ct);

        var ratingsGiven = await _db.UserRatings
            .AsNoTracking()
            .Where(r => r.RaterId == userId)
            .Select(r => new AccountDataExportUserRating
            {
                CounterpartyUserId = r.RatedUserId,
                Credibility = r.Credibility,
                Safety = r.Safety,
                ResponseSpeed = r.ResponseSpeed,
                Transparency = r.Transparency,
                OverallScore = r.OverallScore,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync(ct);

        var ratingsReceived = await _db.UserRatings
            .AsNoTracking()
            .Where(r => r.RatedUserId == userId)
            .Select(r => new AccountDataExportUserRating
            {
                CounterpartyUserId = r.RaterId,
                Credibility = r.Credibility,
                Safety = r.Safety,
                ResponseSpeed = r.ResponseSpeed,
                Transparency = r.Transparency,
                OverallScore = r.OverallScore,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync(ct);

        return new AccountDataExportDto
        {
            ExportedAtUtc = DateTime.UtcNow,
            Account = profile,
            Properties = properties,
            ReviewsWritten = reviews,
            Favorites = favorites,
            VisitRequestsMade = visits,
            Consents = consents,
            RatingsGiven = ratingsGiven,
            RatingsReceived = ratingsReceived
        };
    }
}
