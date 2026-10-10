using MediatR;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Users.Queries.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler
    : IRequestHandler<GetCurrentUserQuery, UserDto?>
{
    private readonly IUserIdentityReadService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly IPlanRepository _plans;
    private readonly IPlanRequestRepository _planRequests;

    public GetCurrentUserQueryHandler(
        IUserIdentityReadService identity,
        IUserAccountRepository accounts,
        IPlanRepository plans,
        IPlanRequestRepository planRequests)
    {
        _planRequests = planRequests;
        _identity = identity;
        _accounts = accounts;
        _plans = plans;
    }

    public async Task<UserDto?> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        var identity = await _identity.FindByIdAsync(
            request.UserId,
            cancellationToken);

        if (identity is null || identity.IsDeleted)
        {
            return null;
        }

        var account = await _accounts.GetByIdAsync(
            identity.UserAccountId,
            cancellationToken);

        if (account is null)
        {
            return null;
        }

        var roles = await _identity.GetRolesAsync(
            identity.IdentityId,
            cancellationToken);

        var planTier = account.PlanId is { } planId
            ? (await _plans.GetByIdAsync(planId, cancellationToken))?.Tier
            : null;

        var pendingRequest = await _planRequests.GetPendingForUserAsync(account.Id, cancellationToken);
        var pendingPlanTier = pendingRequest is null
            ? null
            : (await _plans.GetByIdAsync(pendingRequest.PlanId, cancellationToken))?.Tier;

        return MapToDto(account, identity, roles, planTier, pendingPlanTier);
    }

    private static UserDto MapToDto(
        UserAccount account,
        IdentityAccountSnapshot identity,
        IReadOnlyList<string> roles,
        string? planTier,
        string? pendingPlanTier)
    {
        return new UserDto
        {
            Id = account.Id,
            Email = identity.Email ?? string.Empty,
            FirstName = account.FirstName,
            LastName = account.LastName,
            DisplayName = account.DisplayName,
            PhoneNumber = identity.PhoneNumber,
            ProfileImageUrl = account.ProfileImageUrl,
            PreferredLanguage = account.PreferredLanguage,
            PreferredCurrency = account.PreferredCurrency,
            CountryCode = account.CountryCode,
            // The profile form is filled from this response and saves everything it shows. Leaving
            // these out showed an empty bio and contact line and made the next save clear both
            // (the app sends an empty string for a field it holds as empty).
            Bio = account.Bio,
            ContactInfo = account.ContactInfo,
            EmailConfirmed = identity.EmailConfirmed,
            PhoneConfirmed = identity.PhoneConfirmed,
            HasPassword = identity.HasPassword,
            PlanTier = planTier,
            PendingPlanTier = pendingPlanTier,
            PlanSelectedAt = account.PlanSelectedAt,
            CreatedAt = account.CreatedAt,
            DeletionScheduledFor = account.DeletionScheduledFor,
            Roles = roles.ToList().AsReadOnly()
        };
    }
}
