using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Auth.Enums;

namespace PropertyApi.Application.Auth.Commands.SendPhoneOtp;

/// <summary>
/// أمر CQRS لإرسال رمز OTP إلى رقم هاتف بصيغة E.164.
/// </summary>
public sealed record SendPhoneOtpCommand(
    string PhoneNumber,
    OtpPurpose Purpose = OtpPurpose.PhoneRegistration,
    string? IpAddress = null
) : IRequest<SendOtpResult>;

/// <summary>
/// معالج إرسال OTP.
///
/// القاعدة المهمة هنا:
/// لا نحفظ OTP في قاعدة البيانات إلا بعد نجاح إرسال SMS.
/// السبب: CountRecentAsync يعتمد على السجلات المحفوظة، ولذلك حفظ OTP قبل الإرسال
/// يجعل فشل مزود SMS يُحسب ضد المستخدم في rate limiting.
/// </summary>
public sealed class SendPhoneOtpCommandHandler
    : IRequestHandler<SendPhoneOtpCommand, SendOtpResult>
{
    private const int MaxOtpPerHour = 3;
    private const int OtpExpiryMinutes = 5;
    private const int RetryWindowMinutes = 60;

    // 1 محاولة أصلية + 2 retry = 3 محاولات إجمالاً.
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
                "لقد طلبت كثيراً من الرموز. انتظر ساعة ثم حاول مجدداً.",
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
                "فشل إرسال الرسالة. تأكد من رقم الهاتف أو حاول لاحقاً.");
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
            // لا يمكن حذف SMS بعد إرساله. أفضل تعويض هنا هو عدم إخفاء المشكلة:
            // المستخدم سيحصل على رد فشل، والسجل لن يكون قابلاً للتحقق إن فشل الحفظ.
            _logger.LogError(
                ex,
                "OTP SMS was sent to {Phone}, but persisting OTP failed. User must request a new code.",
                phone);

            return SendOtpResult.Fail(
                "OTP_STORE_FAILED",
                "تم إرسال الرسالة لكن حدث خطأ أثناء حفظ الرمز. حاول طلب رمز جديد.");
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
/// التحقق من صحة الأمر قبل تنفيذه عبر MediatR validation pipeline.
/// </summary>
public sealed class SendPhoneOtpCommandValidator
    : AbstractValidator<SendPhoneOtpCommand>
{
    public SendPhoneOtpCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("رقم الهاتف مطلوب.")
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage(
                "رقم الهاتف يجب أن يكون بصيغة دولية مثل: +963911234567")
            .WithName("PhoneNumber");
    }
}
