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

// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// Ø§Ù„Ø®Ø·ÙˆØ© 2: Ø§Ù„ØªØ­Ù‚Ù‚ Ù…Ù† Ø±Ù…Ø² OTP (Ø§Ù„ØªØ³Ø¬ÙŠÙ„ + Ø§Ù„Ø¯Ø®ÙˆÙ„ ÙÙŠ Ø¢Ù†Ù Ù…Ø¹Ø§Ù‹)
//
// Clean Architecture:
// Ù‡Ø°Ø§ Ø§Ù„Ù€ Handler Ù„Ø§ ÙŠØ¹ØªÙ…Ø¯ Ø¹Ù„Ù‰ concrete identity user service Ù…Ø¨Ø§Ø´Ø±Ø©.
// ÙŠØ³ØªØ®Ø¯Ù… IIdentityUserService ÙÙ‚Ø·ØŒ ÙˆØ§Ù„ØªÙ†ÙÙŠØ° Ø§Ù„Ø­Ù‚ÙŠÙ‚ÙŠ ÙÙŠ Infrastructure.
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
    private readonly ILogger<VerifyPhoneOtpCommandHandler> _logger;

    public VerifyPhoneOtpCommandHandler(
        IOtpCodeRepository otpRepo,
        IOtpService otpService,
        IIdentityUserService identityUsers,
        ITokenService tokenService,
        IRefreshTokenStore refreshTokenStore,
        ILogger<VerifyPhoneOtpCommandHandler> logger)
    {
        _otpRepo = otpRepo;
        _otpService = otpService;
        _identityUsers = identityUsers;
        _tokenService = tokenService;
        _refreshTokenStore = refreshTokenStore;
        _logger = logger;
    }

    public async Task<VerifyOtpResult> Handle(
        VerifyPhoneOtpCommand request,
        CancellationToken ct)
    {
        var phone = request.PhoneNumber.Trim();
        var code = request.Code.Trim();

        // â”€â”€ 1. Ø§Ø¨Ø­Ø« Ø¹Ù† Ø¢Ø®Ø± Ø±Ù…Ø² ØµØ§Ù„Ø­ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var otpCode = await _otpRepo.GetLatestValidAsync(
            phone,
            request.Purpose,
            ct);

        if (otpCode is null)
        {
            return VerifyOtpResult.Fail(
                "OTP_NOT_FOUND",
                "Ù„Ø§ ÙŠÙˆØ¬Ø¯ Ø±Ù…Ø² ØµØ§Ù„Ø­ Ù„Ù‡Ø°Ø§ Ø§Ù„Ø±Ù‚Ù…. Ø§Ø·Ù„Ø¨ Ø±Ù…Ø²Ø§Ù‹ Ø¬Ø¯ÙŠØ¯Ø§Ù‹.");
        }

        if (!otpCode.IsValid())
        {
            var reason = otpCode.IsExpired()
                ? "Ø§Ù†ØªÙ‡Øª ØµÙ„Ø§Ø­ÙŠØ© Ø§Ù„Ø±Ù…Ø²."
                : otpCode.IsUsed
                    ? "Ø§Ù„Ø±Ù…Ø² Ù…ÙØ³ØªØ®Ø¯ÙŽÙ… Ù…Ø³Ø¨Ù‚Ø§Ù‹."
                    : otpCode.IsExhausted()
                        ? "ØªØ¬Ø§ÙˆØ²Øª Ø¹Ø¯Ø¯ Ø§Ù„Ù…Ø­Ø§ÙˆÙ„Ø§Øª."
                        : "Ø§Ù„Ø±Ù…Ø² ØºÙŠØ± ØµØ§Ù„Ø­.";

            return VerifyOtpResult.Fail("OTP_INVALID", reason);
        }

        // â”€â”€ 2. ØªØ­Ù‚Ù‚ Ù…Ù† ØªØ·Ø§Ø¨Ù‚ Ø§Ù„Ø±Ù…Ø² â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var isMatch = _otpService.Verify(code, otpCode.CodeHash);

        if (!isMatch)
        {
            otpCode.IncrementAttempts();
            await _otpRepo.SaveChangesAsync(ct);

            var remaining = Math.Max(0, 3 - otpCode.AttemptCount);

            _logger.LogWarning(
                "Wrong OTP attempt for {Phone}. Remaining: {Remaining}",
                phone,
                remaining);

            return VerifyOtpResult.Fail(
                "OTP_WRONG",
                remaining > 0
                    ? $"Ø§Ù„Ø±Ù…Ø² ØºÙŠØ± ØµØ­ÙŠØ­. ØªØ¨Ù‚Ù‰ Ù„Ùƒ {remaining} Ù…Ø­Ø§ÙˆÙ„Ø©."
                    : "Ø§Ø³ØªÙ†ÙØ¯Øª Ø¬Ù…ÙŠØ¹ Ø§Ù„Ù…Ø­Ø§ÙˆÙ„Ø§Øª. Ø§Ø·Ù„Ø¨ Ø±Ù…Ø²Ø§Ù‹ Ø¬Ø¯ÙŠØ¯Ø§Ù‹.");
        }

        // â”€â”€ 3. Ø§Ù„Ø±Ù…Ø² ØµØ­ÙŠØ­ â€” Ø¶Ø¹ Ø¹Ù„Ø§Ù…Ø© "ØªÙ… Ø§Ù„Ø§Ø³ØªØ®Ø¯Ø§Ù…" â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        otpCode.MarkAsUsed();
        await _otpRepo.SaveChangesAsync(ct);

        // â”€â”€ 4. Ø§Ø¨Ø­Ø« Ø¹Ù† Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… Ø£Ùˆ Ø£Ù†Ø´Ø¦Ù‡ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var (user, isNewUser) = await GetOrCreateUserAsync(request, phone, ct);

        if (user is null)
        {
            return VerifyOtpResult.Fail(
                "USER_CREATE_FAILED",
                "ÙØ´Ù„ Ø¥Ù†Ø´Ø§Ø¡ Ø§Ù„Ø­Ø³Ø§Ø¨. Ø­Ø§ÙˆÙ„ Ù…Ø¬Ø¯Ø¯Ø§Ù‹.");
        }

        // â”€â”€ 5. Ø£ØµØ¯Ø± JWT + Refresh Token â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var roles = await _identityUsers.GetRolesAsync(user, ct);

        var accessToken = _tokenService.GenerateAccessToken(
            user,
            roles.ToArray());

        var refreshToken = _tokenService.GenerateRefreshToken();

        await _refreshTokenStore.StoreAsync(
            user.Id,
            refreshToken,
            request.IpAddress,
            ct);

        _logger.LogInformation(
            "Phone auth successful for {Phone}. IsNewUser: {IsNewUser}",
            phone,
            isNewUser);

        return VerifyOtpResult.Ok(
            isNewUser: isNewUser,
            accessToken: accessToken,
            refreshToken: refreshToken,
            expiresAt: DateTime.UtcNow.AddMinutes(15),
            user: new UserProfileDto
            {
                Id = user.Id,
                PhoneNumber = user.PhoneNumber ?? phone,
                Email = user.Email,
                DisplayName = user.DisplayName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                HasEmail = !string.IsNullOrEmpty(user.Email),
                HasPassword = !string.IsNullOrEmpty(user.PasswordHash),
                EmailVerified = user.EmailConfirmed
            });
    }

    private async Task<(User? user, bool isNewUser)> GetOrCreateUserAsync(
        VerifyPhoneOtpCommand request,
        string phone,
        CancellationToken ct)
    {
        // Ø­Ø§Ù„ÙŠÙ‹Ø§ Ù†Ø³ØªØ®Ø¯Ù… UserName = phone Ù„Ù„Ù…Ø³ØªØ®Ø¯Ù…ÙŠÙ† Ø§Ù„Ù…Ø³Ø¬Ù„ÙŠÙ† Ø¨Ø§Ù„Ù‡Ø§ØªÙ.
        var existingUser = await _identityUsers.FindByUserNameAsync(phone, ct);

        if (existingUser is not null)
        {
            if (!existingUser.PhoneNumberConfirmed)
            {
                existingUser.PhoneNumberConfirmed = true;
                var updateResult = await _identityUsers.UpdateAsync(existingUser, ct);

                if (!updateResult.Succeeded)
                {
                    _logger.LogError(
                        "Failed to update phone confirmation for user {UserId}: {Errors}",
                        existingUser.Id,
                        string.Join(", ", updateResult.Errors));

                    return (null, isNewUser: false);
                }
            }

            return (existingUser, isNewUser: false);
        }

        var newUser = new User
        {
            UserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            Email = null,
            EmailConfirmed = false,
            FirstName = request.FirstName?.Trim() ?? string.Empty,
            LastName = request.LastName?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        var createResult = await _identityUsers.CreateAsync(newUser, ct);

        if (!createResult.Succeeded)
        {
            _logger.LogError(
                "Failed to create phone user: {Errors}",
                string.Join(", ", createResult.Errors));

            return (null, isNewUser: false);
        }

        var roleResult = await _identityUsers.AddToRoleAsync(newUser, RoleNames.User, ct);

        if (!roleResult.Succeeded)
        {
            _logger.LogError(
                "Failed to add default role to phone user {UserId}: {Errors}",
                newUser.Id,
                string.Join(", ", roleResult.Errors));
        }

        return (newUser, isNewUser: true);
    }
}

public sealed class VerifyPhoneOtpCommandValidator
    : AbstractValidator<VerifyPhoneOtpCommand>
{
    public VerifyPhoneOtpCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Ø±Ù‚Ù… Ø§Ù„Ù‡Ø§ØªÙ Ù…Ø·Ù„ÙˆØ¨.")
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage("Ø±Ù‚Ù… Ø§Ù„Ù‡Ø§ØªÙ ÙŠØ¬Ø¨ Ø£Ù† ÙŠÙƒÙˆÙ† Ø¨ØµÙŠØºØ© Ø¯ÙˆÙ„ÙŠØ©.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Ø±Ù…Ø² Ø§Ù„ØªØ­Ù‚Ù‚ Ù…Ø·Ù„ÙˆØ¨.")
            .Length(6)
            .WithMessage("Ø±Ù…Ø² Ø§Ù„ØªØ­Ù‚Ù‚ ÙŠØªÙƒÙˆÙ† Ù…Ù† 6 Ø£Ø±Ù‚Ø§Ù….")
            .Matches(@"^\d{6}$")
            .WithMessage("Ø±Ù…Ø² Ø§Ù„ØªØ­Ù‚Ù‚ ÙŠØ¬Ø¨ Ø£Ù† ÙŠØ­ØªÙˆÙŠ Ø¹Ù„Ù‰ Ø£Ø±Ù‚Ø§Ù… ÙÙ‚Ø·.");

        RuleFor(x => x.FirstName)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.FirstName))
            .WithMessage("Ø§Ù„Ø§Ø³Ù… Ø§Ù„Ø£ÙˆÙ„ ÙŠØ¬Ø¨ Ø£Ù„Ø§ ÙŠØªØ¬Ø§ÙˆØ² 100 Ø­Ø±Ù.");

        RuleFor(x => x.LastName)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.LastName))
            .WithMessage("Ø§Ø³Ù… Ø§Ù„Ø¹Ø§Ø¦Ù„Ø© ÙŠØ¬Ø¨ Ø£Ù„Ø§ ÙŠØªØ¬Ø§ÙˆØ² 100 Ø­Ø±Ù.");

        RuleFor(x => x.Purpose)
            .IsInEnum()
            .WithMessage("ØºØ±Ø¶ Ø±Ù…Ø² Ø§Ù„ØªØ­Ù‚Ù‚ ØºÙŠØ± Ù…Ø¯Ø¹ÙˆÙ….");

    }
}

