using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Auth.Commands.SendPhoneOtp;

/// <summary>
/// Ø£Ù…Ø± CQRS Ù„Ø¥Ø±Ø³Ø§Ù„ Ø±Ù…Ø² OTP Ø¥Ù„Ù‰ Ø±Ù‚Ù… Ù‡Ø§ØªÙ Ø¨ØµÙŠØºØ© E.164.
/// </summary>
public sealed record SendPhoneOtpCommand(
    string PhoneNumber,
    OtpPurpose Purpose = OtpPurpose.PhoneRegistration,
    string? IpAddress = null
) : IRequest<SendOtpResult>;


public sealed class SendPhoneOtpCommandHandler
    : IRequestHandler<SendPhoneOtpCommand, SendOtpResult>
{
    private const int MaxOtpPerHour = 3;
    private const int OtpExpiryMinutes = 5;
    private const int RetryWindowMinutes = 60;

    // 1 Ù…Ø­Ø§ÙˆÙ„Ø© Ø£ØµÙ„ÙŠØ© + 2 retry = 3 Ù…Ø­Ø§ÙˆÙ„Ø§Øª Ø¥Ø¬Ù…Ø§Ù„Ø§Ù‹.
    private const int SmsSendMaxAttempts = 3;

    private readonly IOtpCodeRepository _otpRepo;
    private readonly IOtpService _otpService;
    private readonly ISmsService _smsService;
    private readonly ILogger<SendPhoneOtpCommandHandler> _logger;

    public SendPhoneOtpCommandHandler(
        IOtpCodeRepository otpRepo,
        IOtpService otpService,
        ISmsService smsService,
        ILogger<SendPhoneOtpCommandHandler> logger)
    {
        _otpRepo = otpRepo;
        _otpService = otpService;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task<SendOtpResult> Handle(
        SendPhoneOtpCommand request,
        CancellationToken ct)
    {
        var phone = request.PhoneNumber.Trim();

        var recentCount = await _otpRepo.CountRecentAsync(
            phone,
            TimeSpan.FromMinutes(RetryWindowMinutes),
            ct);

        if (recentCount >= MaxOtpPerHour)
        {
            _logger.LogWarning(
                "Rate limit exceeded for phone {Phone}. Recent OTP count: {RecentCount}",
                phone,
                recentCount);

            return SendOtpResult.Fail(
                "RATE_LIMITED",
                "Ù„Ù‚Ø¯ Ø·Ù„Ø¨Øª ÙƒØ«ÙŠØ±Ø§Ù‹ Ù…Ù† Ø§Ù„Ø±Ù…ÙˆØ². Ø§Ù†ØªØ¸Ø± Ø³Ø§Ø¹Ø© Ø«Ù… Ø­Ø§ÙˆÙ„ Ù…Ø¬Ø¯Ø¯Ø§Ù‹.",
                retryAfter: RetryWindowMinutes * 60);
        }

        var (otp, hash) = _otpService.Generate();

        _logger.LogInformation(
            "Sending OTP SMS to {Phone} for purpose {Purpose}",
            phone,
            request.Purpose);

        var sent = await SendSmsWithRetryAsync(phone, otp, ct);

        if (!sent)
        {
            _logger.LogError(
                "OTP SMS sending failed for phone {Phone} after {AttemptCount} attempts. OTP will not be persisted.",
                phone,
                SmsSendMaxAttempts);

            return SendOtpResult.Fail(
                "SMS_FAILED",
                "ÙØ´Ù„ Ø¥Ø±Ø³Ø§Ù„ Ø§Ù„Ø±Ø³Ø§Ù„Ø©. ØªØ£ÙƒØ¯ Ù…Ù† Ø±Ù‚Ù… Ø§Ù„Ù‡Ø§ØªÙ Ø£Ùˆ Ø­Ø§ÙˆÙ„ Ù„Ø§Ø­Ù‚Ø§Ù‹.");
        }

        var otpCode = OtpCode.Create(
            phoneNumber: phone,
            codeHash: hash,
            purpose: request.Purpose,
            ipAddress: request.IpAddress,
            expiryMinutes: OtpExpiryMinutes);

        try
        {
            await _otpRepo.AddAsync(otpCode, ct);
            await _otpRepo.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Ù„Ø§ ÙŠÙ…ÙƒÙ† Ø­Ø°Ù SMS Ø¨Ø¹Ø¯ Ø¥Ø±Ø³Ø§Ù„Ù‡. Ø£ÙØ¶Ù„ ØªØ¹ÙˆÙŠØ¶ Ù‡Ù†Ø§ Ù‡Ùˆ Ø¹Ø¯Ù… Ø¥Ø®ÙØ§Ø¡ Ø§Ù„Ù…Ø´ÙƒÙ„Ø©:
            // Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… Ø³ÙŠØ­ØµÙ„ Ø¹Ù„Ù‰ Ø±Ø¯ ÙØ´Ù„ØŒ ÙˆØ§Ù„Ø³Ø¬Ù„ Ù„Ù† ÙŠÙƒÙˆÙ† Ù‚Ø§Ø¨Ù„Ø§Ù‹ Ù„Ù„ØªØ­Ù‚Ù‚ Ø¥Ù† ÙØ´Ù„ Ø§Ù„Ø­ÙØ¸.
            _logger.LogError(
                ex,
                "OTP SMS was sent to {Phone}, but persisting OTP failed. User must request a new code.",
                phone);

            return SendOtpResult.Fail(
                "OTP_STORE_FAILED",
                "ØªÙ… Ø¥Ø±Ø³Ø§Ù„ Ø§Ù„Ø±Ø³Ø§Ù„Ø© Ù„ÙƒÙ† Ø­Ø¯Ø« Ø®Ø·Ø£ Ø£Ø«Ù†Ø§Ø¡ Ø­ÙØ¸ Ø§Ù„Ø±Ù…Ø². Ø­Ø§ÙˆÙ„ Ø·Ù„Ø¨ Ø±Ù…Ø² Ø¬Ø¯ÙŠØ¯.");
        }

        _logger.LogInformation(
            "OTP sent and persisted successfully for phone {Phone} and purpose {Purpose}",
            phone,
            request.Purpose);

        return SendOtpResult.Ok();
    }

    private async Task<bool> SendSmsWithRetryAsync(
        string phone,
        string otp,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= SmsSendMaxAttempts; attempt++)
        {
            try
            {
                var sent = await _smsService.SendOtpAsync(phone, otp, ct);

                if (sent)
                {
                    _logger.LogInformation(
                        "OTP SMS sent successfully to {Phone} on attempt {Attempt}",
                        phone,
                        attempt);

                    return true;
                }

                _logger.LogWarning(
                    "OTP SMS provider returned false for phone {Phone} on attempt {Attempt}/{MaxAttempts}",
                    phone,
                    attempt,
                    SmsSendMaxAttempts);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "OTP SMS sending threw exception for phone {Phone} on attempt {Attempt}/{MaxAttempts}",
                    phone,
                    attempt,
                    SmsSendMaxAttempts);
            }
        }

        return false;
    }
}

/// <summary>
/// Ø§Ù„ØªØ­Ù‚Ù‚ Ù…Ù† ØµØ­Ø© Ø§Ù„Ø£Ù…Ø± Ù‚Ø¨Ù„ ØªÙ†ÙÙŠØ°Ù‡ Ø¹Ø¨Ø± MediatR validation pipeline.
/// </summary>
public sealed class SendPhoneOtpCommandValidator
    : AbstractValidator<SendPhoneOtpCommand>
{
    public SendPhoneOtpCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Ø±Ù‚Ù… Ø§Ù„Ù‡Ø§ØªÙ Ù…Ø·Ù„ÙˆØ¨.")
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage(
                "Ø±Ù‚Ù… Ø§Ù„Ù‡Ø§ØªÙ ÙŠØ¬Ø¨ Ø£Ù† ÙŠÙƒÙˆÙ† Ø¨ØµÙŠØºØ© Ø¯ÙˆÙ„ÙŠØ© Ù…Ø«Ù„: +963911234567")
            .WithName("PhoneNumber");
    }
}

