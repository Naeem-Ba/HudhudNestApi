using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Auth.Enums;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;

// ══════════════════════════════════════════════════════════════
// الخطوة 2: التحقق من رمز OTP (التسجيل + الدخول في آنٍ معاً)
//
// هذا الـ Handler هو قلب النظام. يحدد:
//   • هل المستخدم موجود؟ → دخول
//   • هل المستخدم جديد؟  → تسجيل ثم دخول
//
// تسلسل الأحداث:
// 1. ابحث عن آخر رمز صالح لرقم الهاتف
// 2. تحقق من تطابق الرمز المُدخَل
// 3. إذا فشل → سجّل محاولة خاطئة
// 4. إذا نجح → ابحث عن المستخدم أو أنشئه
// 5. أصدر JWT + Refresh Token
// ══════════════════════════════════════════════════════════════

// ── الأمر (Command) ───────────────────────────────────────────
public sealed record VerifyPhoneOtpCommand(
    string PhoneNumber,
    string Code,
    string? FirstName = null,
    string? LastName = null,
    string? IpAddress = null
) : IRequest<VerifyOtpResult>;

// ── معالج الأمر (Handler) ─────────────────────────────────────
public sealed class VerifyPhoneOtpCommandHandler
    : IRequestHandler<VerifyPhoneOtpCommand, VerifyOtpResult>
{
    private readonly IOtpCodeRepository _otpRepo;
    private readonly IOtpService _otpService;
    private readonly UserManager<User> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly ILogger<VerifyPhoneOtpCommandHandler> _logger;

    public VerifyPhoneOtpCommandHandler(
        IOtpCodeRepository otpRepo,
        IOtpService otpService,
        UserManager<User> userManager,
        ITokenService tokenService,
        IRefreshTokenStore refreshTokenStore,
        ILogger<VerifyPhoneOtpCommandHandler> logger)
    {
        _otpRepo = otpRepo;
        _otpService = otpService;
        _userManager = userManager;
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

        // ── 1. ابحث عن آخر رمز صالح ──────────────────────────
        var otpCode = await _otpRepo.GetLatestValidAsync(
            phone,
            OtpPurpose.PhoneRegistration,
            ct);

        if (otpCode is null)
        {
            return VerifyOtpResult.Fail(
                "OTP_NOT_FOUND",
                "لا يوجد رمز صالح لهذا الرقم. اطلب رمزاً جديداً.");
        }

        if (!otpCode.IsValid())
        {
            var reason = otpCode.IsExpired()
                ? "انتهت صلاحية الرمز."
                : otpCode.IsUsed
                    ? "الرمز مُستخدَم مسبقاً."
                    : otpCode.IsExhausted()
                        ? "تجاوزت عدد المحاولات."
                        : "الرمز غير صالح.";

            return VerifyOtpResult.Fail("OTP_INVALID", reason);
        }

        // ── 2. تحقق من تطابق الرمز ───────────────────────────
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
                    ? $"الرمز غير صحيح. تبقى لك {remaining} محاولة."
                    : "استنفدت جميع المحاولات. اطلب رمزاً جديداً.");
        }

        // ── 3. الرمز صحيح — ضع علامة "تم الاستخدام" ──────────
        otpCode.MarkAsUsed();
        await _otpRepo.SaveChangesAsync(ct);

        // ── 4. ابحث عن المستخدم أو أنشئه ────────────────────
        var (user, isNewUser) = await GetOrCreateUserAsync(request, phone);

        if (user is null)
        {
            return VerifyOtpResult.Fail(
                "USER_CREATE_FAILED",
                "فشل إنشاء الحساب. حاول مجدداً.");
        }

        // ── 5. أصدر JWT + Refresh Token ───────────────────────
        var roles = await _userManager.GetRolesAsync(user);

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

    // ── الدالة المساعدة: ابحث عن المستخدم أو أنشئه ────────────
    private async Task<(User? user, bool isNewUser)> GetOrCreateUserAsync(
        VerifyPhoneOtpCommand request,
        string phone)
    {
        // ملاحظة: حاليًا نستخدم UserName = phone للمستخدمين المسجلين بالهاتف.
        // لاحقًا الأفضل إضافة بحث مباشر بـ PhoneNumber لتقليل مخاطر التكرار.
        var existingUser = await _userManager.FindByNameAsync(phone);

        if (existingUser is not null)
        {
            if (!existingUser.PhoneNumberConfirmed)
            {
                existingUser.PhoneNumberConfirmed = true;
                await _userManager.UpdateAsync(existingUser);
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

        var createResult = await _userManager.CreateAsync(newUser);

        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
            {
                _logger.LogError(
                    "Failed to create phone user: {Code} - {Description}",
                    error.Code,
                    error.Description);
            }

            return (null, isNewUser: false);
        }

        var roleResult = await _userManager.AddToRoleAsync(newUser, "User");

        if (!roleResult.Succeeded)
        {
            foreach (var error in roleResult.Errors)
            {
                _logger.LogError(
                    "Failed to add default role to phone user: {Code} - {Description}",
                    error.Code,
                    error.Description);
            }
        }

        return (newUser, isNewUser: true);
    }
}

// ── المُدقق (Validator) ───────────────────────────────────────
public sealed class VerifyPhoneOtpCommandValidator
    : AbstractValidator<VerifyPhoneOtpCommand>
{
    public VerifyPhoneOtpCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("رقم الهاتف مطلوب.")
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage("رقم الهاتف يجب أن يكون بصيغة دولية.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("رمز التحقق مطلوب.")
            .Length(6)
            .WithMessage("رمز التحقق يتكون من 6 أرقام.")
            .Matches(@"^\d{6}$")
            .WithMessage("رمز التحقق يجب أن يحتوي على أرقام فقط.");

        RuleFor(x => x.FirstName)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.FirstName))
            .WithMessage("الاسم الأول يجب ألا يتجاوز 100 حرف.");

        RuleFor(x => x.LastName)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.LastName))
            .WithMessage("اسم العائلة يجب ألا يتجاوز 100 حرف.");
    }
}