using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;

public sealed record VerifyPhoneOtpCommand(
    string PhoneNumber,
    string Code,
    OtpPurpose Purpose = OtpPurpose.PhoneRegistration,
    string? FirstName = null,
    string? LastName = null,
    string? IpAddress = null
) : IRequest<VerifyOtpResult>;

public sealed class VerifyPhoneOtpCommandHandler
    : IRequestHandler<VerifyPhoneOtpCommand, VerifyOtpResult>
{
    private readonly IOtpCodeRepository _otpRepo;
    private readonly IOtpService _otpService;
    private readonly IPhoneOtpIdentityService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly PhoneOtpSessionIssuer _sessionIssuer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<VerifyPhoneOtpCommandHandler> _logger;

    public VerifyPhoneOtpCommandHandler(
        IOtpCodeRepository otpRepo,
        IOtpService otpService,
        IPhoneOtpIdentityService identity,
        IUserAccountRepository accounts,
        PhoneOtpSessionIssuer sessionIssuer,
        IUnitOfWork unitOfWork,
        ILogger<VerifyPhoneOtpCommandHandler> logger)
    {
        _otpRepo = otpRepo;
        _otpService = otpService;
        _identity = identity;
        _accounts = accounts;
        _sessionIssuer = sessionIssuer;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<VerifyOtpResult> Handle(
        VerifyPhoneOtpCommand request,
        CancellationToken ct)
    {
        var phone =
            request.PhoneNumber.Trim();

        var code =
            request.Code.Trim();

        var otpCode =
            await _otpRepo.GetLatestValidAsync(
                phone,
                request.Purpose,
                ct);

        if (otpCode is null)
        {
            return VerifyOtpResult.Fail(
                "OTP_NOT_FOUND",
                "No valid verification code was found. Request a new code.");
        }

        if (!otpCode.IsValid())
        {
            var reason =
                otpCode.IsExpired()
                    ? "The verification code has expired."
                    : otpCode.IsUsed
                        ? "The verification code has already been used."
                        : otpCode.IsExhausted()
                            ? "The maximum number of attempts has been exceeded."
                            : "The verification code is invalid.";

            return VerifyOtpResult.Fail(
                "OTP_INVALID",
                reason);
        }

        var isMatch =
            _otpService.Verify(
                code,
                otpCode.CodeHash);

        if (!isMatch)
        {
            otpCode.IncrementAttempts();

            await _otpRepo.SaveChangesAsync(
                ct);

            var remaining =
                Math.Max(
                    0,
                    3 - otpCode.AttemptCount);

            _logger.LogWarning(
                "Wrong OTP attempt for {Phone}. Remaining attempts: {Remaining}",
                phone,
                remaining);

            return VerifyOtpResult.Fail(
                "OTP_WRONG",
                remaining > 0
                    ? $"The verification code is incorrect. {remaining} attempt(s) remain."
                    : "The maximum number of attempts has been exceeded. Request a new code.");
        }

        await _unitOfWork.BeginTransactionAsync(
            ct);

        try
        {
            /*
             * Atomic OTP consumption.
             *
             * Only one concurrent verification request may consume
             * the same OTP successfully.
             */
            var consumed =
                await _otpRepo.TryConsumeAsync(
                    otpCode.Id,
                    DateTime.UtcNow,
                    ct);

            if (!consumed)
            {
                await _unitOfWork.RollbackTransactionAsync(
                    ct);

                return VerifyOtpResult.Fail(
                    "OTP_ALREADY_USED",
                    "The verification code has already been used.");
            }

            /*
             * Resolve the neutral Identity snapshot.
             *
             * Existing identities:
             * - are found through secure phone lookup;
             * - are rejected when deleted;
             * - have their phone confirmation persisted when necessary.
             *
             * New identities:
             * - receive one shared Guid for Identity and UserAccount;
             * - receive the default User role;
             * - receive one UserAccount profile projection.
             */
            var resolved =
                await GetOrCreateIdentityAsync(
                    request,
                    phone,
                    ct);

            if (resolved.Identity is null)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);

                return VerifyOtpResult.Fail(
                    resolved.ErrorCode ?? "USER_CREATE_FAILED",
                    resolved.ErrorMessage ?? "Could not create or initialize the account.");
            }

            var identity =
                resolved.Identity;

            var account =
                resolved.Account;

            var isNewUser =
                resolved.IsNewUser;

            var sessionResult =
                await _sessionIssuer.IssueAsync(
                    identity,
                    account,
                    isNewUser,
                    phone,
                    request.IpAddress,
                    ct);

            if (!sessionResult.Success)
            {
                await _unitOfWork.RollbackTransactionAsync(
                    ct);

                return sessionResult;
            }

            /*
             * Commit only after all required state has succeeded:
             *
             * - OTP consumption
             * - Identity creation or phone confirmation
             * - mandatory role assignment
             * - UserAccount creation for new identities
             * - UserAccount persistence
             * - refresh-token persistence
             */
            await _unitOfWork.CommitTransactionAsync(
                ct);

            _logger.LogInformation(
                "Phone authentication completed successfully. IdentityId={IdentityId}, IsNewUser={IsNewUser}",
                identity.IdentityId,
                isNewUser);

            return sessionResult;
        }
        catch (OperationCanceledException)
        {
            await _unitOfWork.RollbackTransactionAsync(
                CancellationToken.None);

            throw;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(
                CancellationToken.None);

            _logger.LogError(
                ex,
                "Atomic phone authentication failed.");

            return VerifyOtpResult.Fail(
                "PHONE_AUTH_FAILED",
                "Could not complete phone authentication.");
        }
    }

    private async Task<ResolvedPhoneIdentity>
        GetOrCreateIdentityAsync(
            VerifyPhoneOtpCommand request,
            string phone,
            CancellationToken ct)
    {
        var existingIdentity =
            await _identity.FindByPhoneNumberAsync(
                phone,
                ct);

        if (existingIdentity is not null)
        {
            if (existingIdentity.IsDeleted)
            {
                _logger.LogWarning(
                    "Phone authentication rejected for deleted identity {IdentityId}.",
                    existingIdentity.IdentityId);

                return ResolvedPhoneIdentity.Failed();
            }

            if (!existingIdentity.PhoneConfirmed)
            {
                var confirmationResult =
                    await _identity.ConfirmPhoneNumberAsync(
                        existingIdentity.IdentityId,
                        DateTime.UtcNow,
                        ct);

                if (!confirmationResult.Succeeded)
                {
                    _logger.LogError(
                        "Failed to confirm phone for identity {IdentityId}. Errors: {Errors}",
                        existingIdentity.IdentityId,
                        string.Join(
                            ", ",
                            confirmationResult.Errors));

                    return ResolvedPhoneIdentity.Failed();
                }

                existingIdentity =
                    existingIdentity with
                    {
                        PhoneConfirmed = true
                    };
            }

            /*
             * Existing production identities should already have their
             * UserAccount projection through the profile backfill.
             *
             * Authentication must still succeed when the profile projection
             * is temporarily absent, because profile data is not required
             * to prove the phone identity.
             */
            var existingAccount =
                await _accounts.GetByIdAsync(
                    existingIdentity.UserAccountId,
                    ct);

            return new ResolvedPhoneIdentity(
                Identity:
                    existingIdentity,

                Account:
                    existingAccount,

                IsNewUser:
                    false);
        }

        return await CreateNewPhoneIdentityAsync(
            request,
            phone,
            ct);
    }

    private async Task<ResolvedPhoneIdentity>
        CreateNewPhoneIdentityAsync(
            VerifyPhoneOtpCommand request,
            string phone,
            CancellationToken ct)
    {
        var now =
            DateTime.UtcNow;

        /*
         * FirstName and LastName are optional at command level because
         * existing users do not need to submit them again.
         *
         * They are mandatory when creating a new business profile.
         */
        var firstName =
            request.FirstName?.Trim();

        var lastName =
            request.LastName?.Trim();

        if (string.IsNullOrWhiteSpace(firstName) ||
            string.IsNullOrWhiteSpace(lastName))
        {
            _logger.LogWarning(
                "New phone identity creation rejected because profile names are missing.");

            return ResolvedPhoneIdentity.Failed(
                errorCode: "PROFILE_NAME_REQUIRED",
                errorMessage: "First name and last name are required to create a new account.");
        }

        var identityId =
            Guid.NewGuid();

        var createRequest =
            new CreateIdentityAccount(
                UserAccountId:
                    identityId,

                Email:
                    null,

                PhoneNumber:
                    phone,

                Password:
                    null,

                LegacyFirstName:
                    firstName,

                LegacyLastName:
                    lastName,

                LegacyCreatedAtUtc:
                    now,

                EmailConfirmed:
                    false,

                LegacyProfileImageUrl:
                    null,

                PhoneConfirmed:
                    true);

        /*
         * Step 1:
         * Create the Identity account.
         *
         * PureIdentityService is responsible for:
         * - PhoneNumber
         * - PhoneNumberLookupHash
         * - PhoneNumberConfirmed
         */
        var createResult =
            await _identity.CreateAsync(
                createRequest,
                ct);

        if (!createResult.Succeeded)
        {
            _logger.LogError(
                "Failed to create phone identity. Errors: {Errors}",
                string.Join(
                    ", ",
                    createResult.Errors));

            return ResolvedPhoneIdentity.Failed();
        }

        /*
         * Step 2:
         * Assign the mandatory default User role.
         */
        var roleResult =
            await _identity.AddToRoleAsync(
                identityId,
                RoleNames.User,
                ct);

        if (!roleResult.Succeeded)
        {
            _logger.LogError(
                "Failed to add the default role to phone identity {IdentityId}. Errors: {Errors}",
                identityId,
                string.Join(
                    ", ",
                    roleResult.Errors));

            /*
             * The caller rolls back the transaction, reverting:
             *
             * - Identity creation
             * - any role changes
             * - OTP consumption
             */
            return ResolvedPhoneIdentity.Failed();
        }

        /*
         * Step 3:
         * Create the business profile projection.
         *
         * Migration invariant:
         *
         * IdentityId == UserAccountId
         *
         * Authentication/security state and profile state are still
         * logically separated even while sharing the same Guid.
         */
        var account =
            UserAccount.Create(
                identityId,
                firstName,
                lastName,
                now);

        await _accounts.AddAsync(
            account,
            ct);

        /*
         * IUserAccountRepository.AddAsync tracks the entity.
         *
         * Persist it inside the current transaction before token
         * persistence and the final commit.
         */
        await _unitOfWork.SaveChangesAsync(
            ct);

        /*
         * Re-read the neutral snapshot after creation because token
         * generation requires UserName and SecurityStamp.
         */
        var createdIdentity =
            await _identity.FindByIdAsync(
                identityId,
                ct);

        if (createdIdentity is null)
        {
            _logger.LogError(
                "Phone identity {IdentityId} was created but could not be reloaded.",
                identityId);

            return ResolvedPhoneIdentity.Failed();
        }

        return new ResolvedPhoneIdentity(
            Identity:
                createdIdentity,

            Account:
                account,

            IsNewUser:
                true);
    }

    private sealed record ResolvedPhoneIdentity(
        IdentityAccountSnapshot? Identity,
        UserAccount? Account,
        bool IsNewUser,
        string? ErrorCode = null,
        string? ErrorMessage = null)
    {
        public static ResolvedPhoneIdentity Failed(
            string? errorCode = null,
            string? errorMessage = null)
            => new(
                Identity: null,
                Account: null,
                IsNewUser: false,
                ErrorCode: errorCode,
                ErrorMessage: errorMessage);
    }
}
