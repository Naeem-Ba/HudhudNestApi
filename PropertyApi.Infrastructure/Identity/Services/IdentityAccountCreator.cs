using Microsoft.AspNetCore.Identity;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Identity.Services;

public sealed class IdentityAccountCreator
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IPhoneNumberLookupHasher _phoneLookupHasher;

    public IdentityAccountCreator(
        UserManager<ApplicationUser> users,
        IPhoneNumberLookupHasher phoneLookupHasher)
    {
        _users = users;
        _phoneLookupHasher = phoneLookupHasher;
    }

    public async Task<IdentityOperationResult> CreateAsync(
        CreateIdentityAccount request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var now = request.LegacyCreatedAtUtc ?? DateTime.UtcNow;
        var verifiedAt = request.PhoneConfirmed
            ? new DateTimeOffset(now, TimeSpan.Zero)
            : (DateTimeOffset?)null;
        var user = new ApplicationUser
        {
            Id = request.UserAccountId,
            UserName = request.Email ?? request.PhoneNumber ?? request.UserAccountId.ToString("N"),
            Email = request.Email,
            EmailConfirmed = request.EmailConfirmed,
            PhoneNumber = request.PhoneNumber,
            PhoneNumberLookupHash = string.IsNullOrWhiteSpace(request.PhoneNumber)
                ? null
                : _phoneLookupHasher.Compute(request.PhoneNumber),
            NormalizedPhoneNumber = request.PhoneNumber,
            PhoneNumberConfirmed = request.PhoneConfirmed,
            PhoneLastVerifiedAtUtc = verifiedAt,
            PhoneVerificationDueAtUtc = verifiedAt?.AddDays(180),
            PhoneVerificationGraceEndsAtUtc = verifiedAt?.AddDays(183),
            PhoneVerificationState = request.PhoneConfirmed
                ? PropertyApi.Domain.Enums.PhoneVerificationState.Verified
                : PropertyApi.Domain.Enums.PhoneVerificationState.NotConfigured,
            CreatedAt = now,
            UpdatedAt = now
        };
        var result = string.IsNullOrWhiteSpace(request.Password)
            ? await _users.CreateAsync(user)
            : await _users.CreateAsync(user, request.Password);
        return IdentityAdapterMapping.Result(result);
    }
}
