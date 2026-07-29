using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Auth.Policies;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Observability;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Orchestration;

public sealed class PhoneAccountMutationCoordinator
{
    private readonly IPhoneOtpIdentityService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly PhoneOwnershipPolicy _ownership;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PhoneAccountMutationCoordinator> _logger;

    public PhoneAccountMutationCoordinator(
        IPhoneOtpIdentityService identity,
        IUserAccountRepository accounts,
        PhoneOwnershipPolicy ownership,
        IUnitOfWork unitOfWork,
        ILogger<PhoneAccountMutationCoordinator> logger)
    {
        _identity = identity;
        _accounts = accounts;
        _ownership = ownership;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ResolvedPhoneAccount> ResolveOrCreateAsync(
        VerifyPhoneOtpCommand command,
        string phone,
        CancellationToken cancellationToken)
    {
        var existing = await _identity.FindByPhoneNumberAsync(phone, cancellationToken);
        var ownership = _ownership.Evaluate(existing);
        if (ownership == PhoneOwnershipDecision.Forbidden)
        {
            return ResolvedPhoneAccount.Failed("ACCOUNT_UNAVAILABLE", "The account is not available.");
        }

        if (existing is not null)
        {
            if (!existing.PhoneConfirmed)
            {
                var confirmed = await _identity.ConfirmPhoneNumberAsync(
                    existing.IdentityId,
                    DateTime.UtcNow,
                    cancellationToken);
                if (!confirmed.Succeeded)
                {
                    return ResolvedPhoneAccount.Failed();
                }

                existing = existing with { PhoneConfirmed = true };
            }

            var profile = await _accounts.GetByIdAsync(
                existing.UserAccountId,
                cancellationToken);
            return new(existing, profile, false);
        }

        return await CreateAsync(command, phone, cancellationToken);
    }

    private async Task<ResolvedPhoneAccount> CreateAsync(
        VerifyPhoneOtpCommand command,
        string phone,
        CancellationToken cancellationToken)
    {
        var firstName = command.FirstName?.Trim();
        var lastName = command.LastName?.Trim();
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            return ResolvedPhoneAccount.Failed(
                "PROFILE_NAME_REQUIRED",
                "First name and last name are required to create a new account.");
        }

        var now = DateTime.UtcNow;
        var identityId = Guid.NewGuid();
        var created = await _identity.CreateAsync(
            new CreateIdentityAccount(
                identityId,
                null,
                phone,
                null,
                firstName,
                lastName,
                now,
                PhoneConfirmed: true),
            cancellationToken);
        if (!created.Succeeded)
        {
            ApplicationTelemetry.RecordAuthenticationStage("account_creation", "failed", "phone_otp");
            _logger.LogWarning(
                "Phone identity creation failed. ErrorCount={ErrorCount}",
                created.Errors.Count);
            return ResolvedPhoneAccount.Failed();
        }

        var role = await _identity.AddToRoleAsync(
            identityId,
            RoleNames.User,
            cancellationToken);
        if (!role.Succeeded)
        {
            return ResolvedPhoneAccount.Failed("ROLE_ASSIGNMENT_FAILED", "Could not initialize the account.");
        }

        var account = UserAccount.Create(identityId, firstName, lastName, now);
        await _accounts.AddAsync(account, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var identity = await _identity.FindByIdAsync(identityId, cancellationToken);
        ApplicationTelemetry.RecordAuthenticationStage(
            "account_creation",
            identity is null ? "failed" : "success",
            "phone_otp");
        return identity is null
            ? ResolvedPhoneAccount.Failed()
            : new(identity, account, true);
    }
}
