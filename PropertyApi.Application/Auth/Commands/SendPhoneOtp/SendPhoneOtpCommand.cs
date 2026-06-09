using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Auth.Enums;

namespace PropertyApi.Application.Auth.Commands.SendPhoneOtp;

// ══════════════════════════════════════════════════════════════
// الخطوة 1: إرسال رمز OTP للهاتف
//
// تسلسل الأحداث:
// 1. المستخدم يُدخل رقم هاتفه
// 2. نتحقق من حد الطلبات (3 رسائل كحد أقصى/ساعة)
// 3. نُولّد رمزاً عشوائياً من 6 أرقام
// 4. نحفظ هاش الرمز في قاعدة البيانات
// 5. نُرسل الرمز عبر SMS
// ══════════════════════════════════════════════════════════════

// ── الأمر (Command) ───────────────────────────────────────────
/// <summary>
/// أمر CQRS لإرسال رمز OTP
/// </summary>
public sealed record SendPhoneOtpCommand(
    string PhoneNumber,
    OtpPurpose Purpose = OtpPurpose.PhoneRegistration,
    string? IpAddress = null
) : IRequest<SendOtpResult>;

// ── معالج الأمر (Handler) ─────────────────────────────────────
public sealed class SendPhoneOtpCommandHandler
    : IRequestHandler<SendPhoneOtpCommand, SendOtpResult>
{
    // ── حدود الأمان ─────────────────────────────────────────
    private const int MaxOtpPerHour = 3;    // أقصى 3 رسائل في الساعة
    private const int OtpExpiryMinutes = 5;    // الرمز صالح 5 دقائق
    private const int RetryWindowMinutes = 60;   // نافذة إعادة المحاولة

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

        // ── 1. فحص حد الطلبات ─────────────────────────────────
        var recentCount = await _otpRepo.CountRecentAsync(
            phone,
            TimeSpan.FromMinutes(RetryWindowMinutes),
            ct);

        if (recentCount >= MaxOtpPerHour)
        {
            _logger.LogWarning(
                "Rate limit exceeded for phone {Phone}", phone);

            return SendOtpResult.Fail(
                "RATE_LIMITED",
                "لقد طلبت كثيراً من الرموز. انتظر ساعة ثم حاول مجدداً.",
                retryAfter: RetryWindowMinutes * 60);
        }

        // ── 2. توليد رمز OTP وهاشه ───────────────────────────
        var (otp, hash) = _otpService.Generate();

        // ── 3. حفظ الهاش في قاعدة البيانات ──────────────────
        var otpCode = OtpCode.Create(
            phoneNumber: phone,
            codeHash: hash,
            purpose: request.Purpose,
            ipAddress: request.IpAddress,
            expiryMinutes: OtpExpiryMinutes);

        await _otpRepo.AddAsync(otpCode, ct);
        await _otpRepo.SaveChangesAsync(ct);

        // ── 4. إرسال SMS ──────────────────────────────────────
        var sent = await _smsService.SendOtpAsync(phone, otp, ct);

        if (!sent)
        {
            _logger.LogError(
                "SMS sending failed for phone {Phone}", phone);

            return SendOtpResult.Fail(
                "SMS_FAILED",
                "فشل إرسال الرسالة. تأكد من رقم الهاتف أو حاول لاحقاً.");
        }

        _logger.LogInformation(
            "OTP sent successfully to {Phone} for purpose {Purpose}",
            phone, request.Purpose);

        return SendOtpResult.Ok();
    }
}

// ── المُدقق (Validator) ───────────────────────────────────────
/// <summary>
/// التحقق من صحة الأمر قبل تنفيذه
/// يعمل تلقائياً عبر ValidationBehavior في MediatR Pipeline
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

        // مثال لرقم سوري: +963 + 9 أرقام
        // مثال لرقم ألماني: +49 + 10 أرقام
        // الصيغة E.164: + ثم 8-15 رقم
    }
}