using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
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
    private readonly IIdentityUserService _identityUsers;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<VerifyPhoneOtpCommandHandler> _logger;

    public VerifyPhoneOtpCommandHandler(
        IOtpCodeRepository otpRepo,
        IOtpService otpService,
        IIdentityUserService identityUsers,
        ITokenService tokenService,
        IRefreshTokenStore refreshTokenStore,
        IUnitOfWork unitOfWork,
        ILogger<VerifyPhoneOtpCommandHandler> logger)
    {
        _otpRepo = otpRepo;
        _otpService = otpService;
        _identityUsers = identityUsers;
        _tokenService = tokenService;
        _refreshTokenStore = refreshTokenStore;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<VerifyOtpResult> Handle(
        VerifyPhoneOtpCommand request,
        CancellationToken ct)
    {
        var phone = request.PhoneNumber.Trim();
        var code = request.Code.Trim();

        var otpCode = await _otpRepo.GetLatestValidAsync(
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
            var reason = otpCode.IsExpired()
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

        var isMatch = _otpService.Verify(
            code,
            otpCode.CodeHash);

        if (!isMatch)
        {
            otpCode.IncrementAttempts();
            await _otpRepo.SaveChangesAsync(ct);

            var remaining = Math.Max(
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

        await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            /*
             * This is an atomic conditional update.
             * Only one concurrent request can consume the OTP.
             */
            var consumed = await _otpRepo.TryConsumeAsync(
                otpCode.Id,
                DateTime.UtcNow,
                ct);

            if (!consumed)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);

                return VerifyOtpResult.Fail(
                    "OTP_ALREADY_USED",
                    "The verification code has already been used.");
            }

            var (user, isNewUser) =
                await GetOrCreateUserAsync(
                    request,
                    phone,
                    ct);

            if (user is null)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);

                return VerifyOtpResult.Fail(
                    "USER_CREATE_FAILED",
                    "Could not create or initialize the account.");
            }

            var roles = await _identityUsers.GetRolesAsync(
                user,
                ct);

            /*
             * A newly-created account must always have the default role.
             * Do not issue tokens for a partially initialized account.
             */
            if (isNewUser &&
                !roles.Contains(
                    RoleNames.User,
                    StringComparer.OrdinalIgnoreCase))
            {
                await _unitOfWork.RollbackTransactionAsync(ct);

                _logger.LogError(
                    "New phone user {UserId} does not have the required default role.",
                    user.Id);

                return VerifyOtpResult.Fail(
                    "ROLE_ASSIGNMENT_FAILED",
                    "Could not initialize the account.");
            }

            var accessToken =
                _tokenService.GenerateAccessToken(
                    user,
                    roles.ToArray());

            var refreshToken =
                _tokenService.GenerateRefreshToken();

            await _refreshTokenStore.StoreAsync(
                user.Id,
                refreshToken,
                request.IpAddress,
                ct);

            /*
             * Commit only after:
             * - OTP consumption
             * - user creation/update
             * - role assignment
             * - refresh-token persistence
             */
            await _unitOfWork.CommitTransactionAsync(ct);

            _logger.LogInformation(
                "Phone authentication completed successfully. UserId={UserId}, IsNewUser={IsNewUser}",
                user.Id,
                isNewUser);

            return VerifyOtpResult.Ok(
                isNewUser: isNewUser,
                accessToken: accessToken,
                refreshToken: refreshToken,
                expiresAt:
                    _tokenService.GetAccessTokenExpiresAtUtc(),
                user: new UserProfileDto
                {
                    Id = user.Id,
                    PhoneNumber =
                        user.PhoneNumber ?? phone,
                    Email = user.Email,
                    DisplayName = user.DisplayName,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    HasEmail =
                        !string.IsNullOrWhiteSpace(user.Email),
                    HasPassword =
                        !string.IsNullOrWhiteSpace(user.PasswordHash),
                    EmailVerified =
                        user.EmailConfirmed
                });
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

    private async Task<(User? user, bool isNewUser)>
        GetOrCreateUserAsync(
            VerifyPhoneOtpCommand request,
            string phone,
            CancellationToken ct)
    {
        var existingUser =
            await _identityUsers.FindByUserNameAsync(
                phone,
                ct);

        if (existingUser is not null)
        {
            if (existingUser.IsDeleted)
            {
                _logger.LogWarning(
                    "Phone authentication rejected for deleted user {UserId}.",
                    existingUser.Id);

                return (null, isNewUser: false);
            }

            if (!existingUser.PhoneNumberConfirmed)
            {
                existingUser.PhoneNumberConfirmed = true;
                existingUser.UpdatedAt = DateTime.UtcNow;

                var updateResult =
                    await _identityUsers.UpdateAsync(
                        existingUser,
                        ct);

                if (!updateResult.Succeeded)
                {
                    _logger.LogError(
                        "Failed to confirm phone for user {UserId}. Errors: {Errors}",
                        existingUser.Id,
                        string.Join(
                            ", ",
                            updateResult.Errors));

                    return (null, isNewUser: false);
                }
            }

            return (
                existingUser,
                isNewUser: false);
        }

        var now = DateTime.UtcNow;

        var newUser = new User
        {
            UserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            Email = null,
            EmailConfirmed = false,
            FirstName =
                request.FirstName?.Trim() ?? string.Empty,
            LastName =
                request.LastName?.Trim() ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        var createResult =
            await _identityUsers.CreateAsync(
                newUser,
                ct);

        if (!createResult.Succeeded)
        {
            _logger.LogError(
                "Failed to create phone user. Errors: {Errors}",
                string.Join(
                    ", ",
                    createResult.Errors));

            return (null, isNewUser: false);
        }

        var roleResult =
            await _identityUsers.AddToRoleAsync(
                newUser,
                RoleNames.User,
                ct);

        if (!roleResult.Succeeded)
        {
            _logger.LogError(
                "Failed to add the default role to phone user {UserId}. Errors: {Errors}",
                newUser.Id,
                string.Join(
                    ", ",
                    roleResult.Errors));

            /*
             * Returning null causes Handle() to roll back:
             * - user creation
             * - OTP consumption
             */
            return (null, isNewUser: false);
        }

        return (
            newUser,
            isNewUser: true);
    }
}

public sealed class VerifyPhoneOtpCommandValidator
    : AbstractValidator<VerifyPhoneOtpCommand>
{
    public VerifyPhoneOtpCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Phone number is required.")
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage(
                "Phone number must use international E.164 format, for example +963911234567.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Verification code is required.")
            .Length(6)
            .WithMessage(
                "Verification code must contain exactly six digits.")
            .Matches(@"^\d{6}$")
            .WithMessage(
                "Verification code must contain digits only.");

        RuleFor(x => x.FirstName)
            .MaximumLength(100)
            .When(x =>
                !string.IsNullOrWhiteSpace(x.FirstName))
            .WithMessage(
                "First name must not exceed 100 characters.");

        RuleFor(x => x.LastName)
            .MaximumLength(100)
            .When(x =>
                !string.IsNullOrWhiteSpace(x.LastName))
            .WithMessage(
                "Last name must not exceed 100 characters.");

        RuleFor(x => x.Purpose)
            .IsInEnum()
            .WithMessage(
                "The OTP purpose is not supported.");
    }
}
