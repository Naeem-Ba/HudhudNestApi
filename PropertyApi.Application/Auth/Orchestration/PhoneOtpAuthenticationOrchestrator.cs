using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Abstractions;
using PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Observability;

namespace PropertyApi.Application.Auth.Orchestration;

public sealed class PhoneOtpAuthenticationOrchestrator : IPhoneOtpAuthenticationOrchestrator
{
    private readonly OtpConsumptionService _otp;
    private readonly PhoneAccountMutationCoordinator _accounts;
    private readonly IAuthenticationSessionIssuer _sessions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PhoneOtpAuthenticationOrchestrator> _logger;

    public PhoneOtpAuthenticationOrchestrator(
        OtpConsumptionService otp,
        PhoneAccountMutationCoordinator accounts,
        IAuthenticationSessionIssuer sessions,
        IUnitOfWork unitOfWork,
        ILogger<PhoneOtpAuthenticationOrchestrator> logger)
    {
        _otp = otp;
        _accounts = accounts;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<VerifyOtpResult> AuthenticateAsync(
        VerifyPhoneOtpCommand command,
        CancellationToken cancellationToken)
    {
        var phone = command.PhoneNumber.Trim();
        var validation = await _otp.ValidateAsync(command, phone, cancellationToken);
        ApplicationTelemetry.RecordAuthenticationStage(
            "phone_verification",
            validation.Kind.ToString().ToLowerInvariant(),
            "phone_otp");
        if (validation.Kind != OtpValidationKind.Valid)
        {
            return VerifyOtpResult.Fail(validation.ErrorCode!, validation.ErrorMessage!);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await _otp.TryConsumeAsync(validation.OtpCodeId!.Value, cancellationToken))
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return VerifyOtpResult.Fail(
                    "OTP_ALREADY_USED",
                    "The verification code has already been used.");
            }

            var resolved = await _accounts.ResolveOrCreateAsync(
                command,
                phone,
                cancellationToken);
            if (resolved.Identity is null)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return VerifyOtpResult.Fail(resolved.ErrorCode!, resolved.ErrorMessage!);
            }

            var session = await _sessions.IssueAsync(
                new AuthenticationSessionRequest(
                    resolved.Identity,
                    "phone_otp",
                    command.IpAddress,
                    SuccessfulLoginRecordingMode.None),
                cancellationToken);
            if (!session.Succeeded)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return VerifyOtpResult.Fail(
                    "SESSION_ISSUANCE_FAILED",
                    "Could not issue the authentication session.");
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            _logger.LogInformation(
                "Phone OTP authentication completed. IdentityId={IdentityId}, IsNewUser={IsNewUser}",
                resolved.Identity.IdentityId,
                resolved.IsNewUser);
            return VerifyOtpResult.Ok(
                resolved.IsNewUser,
                session.AccessToken!,
                session.RefreshToken!,
                session.AccessTokenExpiresAtUtc,
                BuildProfile(resolved, phone));
        }
        catch (OperationCanceledException)
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _logger.LogError(exception, "Atomic phone OTP authentication failed.");
            return VerifyOtpResult.Fail(
                "PHONE_AUTH_FAILED",
                "Could not complete phone authentication.");
        }
    }

    private static UserProfileDto BuildProfile(
        ResolvedPhoneAccount resolved,
        string phone) =>
        new()
        {
            Id = resolved.Identity!.UserAccountId,
            PhoneNumber = resolved.Identity.PhoneNumber ?? phone,
            Email = resolved.Identity.Email,
            DisplayName = resolved.Account?.DisplayName,
            FirstName = resolved.Account?.FirstName ?? string.Empty,
            LastName = resolved.Account?.LastName ?? string.Empty,
            HasEmail = !string.IsNullOrWhiteSpace(resolved.Identity.Email),
            HasPassword = resolved.Identity.HasPassword,
            EmailVerified = resolved.Identity.EmailConfirmed
        };
}
