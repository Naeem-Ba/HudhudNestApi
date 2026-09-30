using MediatR;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Users.Queries.GetUserProfile;

/// <summary>
/// Public profile page data: identity/account resolution mirrors
/// GetUserByIdQueryHandler exactly (same privacy rules — no Email, ever), then
/// layers on the platform-activity stats (sold/rented counts) and the
/// aggregated user-rating scores that are unique to the profile page.
/// </summary>
public sealed class GetUserProfileQueryHandler
    : IRequestHandler<GetUserProfileQuery, UserProfileDto?>
{
    private readonly IUserIdentityReadService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly IPropertyRepository _properties;
    private readonly IUserRatingRepository _ratings;

    public GetUserProfileQueryHandler(
        IUserIdentityReadService identity,
        IUserAccountRepository accounts,
        IPropertyRepository properties,
        IUserRatingRepository ratings)
    {
        _identity = identity;
        _accounts = accounts;
        _properties = properties;
        _ratings = ratings;
    }

    public async Task<UserProfileDto?> Handle(
        GetUserProfileQuery request,
        CancellationToken ct)
    {
        var identity = await _identity.FindByIdAsync(request.UserId, ct);

        if (identity is null || identity.IsDeleted)
        {
            return null;
        }

        var account = await _accounts.GetByIdAsync(identity.UserAccountId, ct);
        var roles = await _identity.GetRolesAsync(identity.IdentityId, ct);

        var isAgent = roles.Any(role =>
            string.Equals(role, RoleNames.Agent, StringComparison.OrdinalIgnoreCase));

        var (soldCount, rentedCount) = await _properties.GetDealCountsByOwnerAsync(
            request.UserId, ct);

        var averages = await _ratings.GetAveragesAsync(request.UserId, ct);

        return new UserProfileDto
        {
            Id = request.UserId,
            DisplayName = BuildDisplayName(account),
            ProfileImageUrl = account?.ProfileImageUrl,
            // account should always exist for a real user; DateTime.UtcNow is
            // only a defensive fallback for the (shouldn't-happen) orphaned-
            // identity case, same defensiveness BuildDisplayName below applies.
            MemberSince = account?.CreatedAt ?? DateTime.UtcNow,
            IsAgent = isAgent,
            Bio = account?.Bio,
            ContactInfo = account?.ContactInfo,
            SoldCount = soldCount,
            RentedCount = rentedCount,
            AverageCredibility = Math.Round(averages.Credibility, 1),
            AverageSafety = Math.Round(averages.Safety, 1),
            AverageResponseSpeed = Math.Round(averages.ResponseSpeed, 1),
            AverageTransparency = Math.Round(averages.Transparency, 1),
            AverageInformationAccuracy = averages.InformationAccuracy.HasValue
                ? Math.Round(averages.InformationAccuracy.Value, 1)
                : null,
            AverageConduct = averages.Conduct.HasValue
                ? Math.Round(averages.Conduct.Value, 1)
                : null,
            AverageOverall = Math.Round(averages.Overall, 1),
            RatingsCount = averages.TotalCount
        };
    }

    private static string BuildDisplayName(UserAccount? account)
    {
        if (account is null)
        {
            return "User";
        }

        if (!string.IsNullOrWhiteSpace(account.DisplayName))
        {
            return account.DisplayName;
        }

        var fullName = $"{account.FirstName} {account.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "User" : fullName;
    }
}
