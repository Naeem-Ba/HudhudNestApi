using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Orchestration;

public sealed class OtpConsumptionService
{
    private readonly IOtpCodeRepository _repository;
    private readonly IOtpService _codes;
    private readonly ILogger<OtpConsumptionService> _logger;

    public OtpConsumptionService(
        IOtpCodeRepository repository,
        IOtpService codes,
        ILogger<OtpConsumptionService> logger)
    {
        _repository = repository;
        _codes = codes;
        _logger = logger;
    }

    public async Task<OtpValidationResult> ValidateAsync(
        VerifyPhoneOtpCommand command,
        string normalizedPhone,
        CancellationToken cancellationToken)
    {
        var otp = await _repository.GetLatestValidAsync(
            normalizedPhone,
            command.Purpose,
            cancellationToken);
        if (otp is null)
        {
            return Failed(OtpValidationKind.NotFound, "OTP_NOT_FOUND",
                "No valid verification code was found. Request a new code.");
        }

        if (!otp.IsValid())
        {
            if (otp.IsExpired())
                return Failed(OtpValidationKind.Expired, "OTP_INVALID", "The verification code has expired.");
            if (otp.IsUsed)
                return Failed(OtpValidationKind.AlreadyConsumed, "OTP_INVALID", "The verification code has already been used.");
            if (otp.IsExhausted())
                return Failed(OtpValidationKind.AttemptLimitExceeded, "OTP_INVALID", "The maximum number of attempts has been exceeded.");
            return Failed(OtpValidationKind.Invalid, "OTP_INVALID", "The verification code is invalid.");
        }

        if (_codes.Verify(command.Code.Trim(), otp.CodeHash))
        {
            return new(OtpValidationKind.Valid, otp.Id);
        }

        otp.IncrementAttempts();
        await _repository.SaveChangesAsync(cancellationToken);
        var remaining = Math.Max(0, 3 - otp.AttemptCount);
        _logger.LogWarning(
            "OTP validation failed. RemainingAttempts={RemainingAttempts}",
            remaining);
        return Failed(
            OtpValidationKind.WrongCode,
            "OTP_WRONG",
            remaining > 0
                ? $"The verification code is incorrect. {remaining} attempt(s) remain."
                : "The maximum number of attempts has been exceeded. Request a new code.");
    }

    public Task<bool> TryConsumeAsync(
        Guid otpCodeId,
        CancellationToken cancellationToken) =>
        _repository.TryConsumeAsync(otpCodeId, DateTime.UtcNow, cancellationToken);

    private static OtpValidationResult Failed(
        OtpValidationKind kind,
        string code,
        string message) =>
        new(kind, ErrorCode: code, ErrorMessage: message);
}
