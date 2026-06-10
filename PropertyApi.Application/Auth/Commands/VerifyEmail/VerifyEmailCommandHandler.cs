using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Auth.Commands.VerifyEmail;

/// <summary>
/// معالج تفعيل البريد الإلكتروني.
/// يعتمد على IIdentityUserService بدلاً من UserManager حتى لا تعرف Application تفاصيل ASP.NET Identity.
/// </summary>
public sealed class VerifyEmailCommandHandler
    : IRequestHandler<VerifyEmailCommand, VerifyEmailResult>
{
    private readonly IIdentityUserService _identityUsers;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        IIdentityUserService identityUsers,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _logger = logger;
    }

    public async Task<VerifyEmailResult> Handle(
        VerifyEmailCommand command,
        CancellationToken ct)
    {
        // ── 1. ابحث عن المستخدم ───────────────────────────────
        var user = await _identityUsers.FindByIdAsync(command.UserId, ct);

        if (user is null || user.IsDeleted)
        {
            _logger.LogWarning(
                "VerifyEmail: User not found [{UserId}]",
                command.UserId);

            return VerifyEmailResult.Fail(
                "INVALID_TOKEN",
                "رابط التحقق غير صالح أو منتهي الصلاحية.");
        }

        // ── 2. هل البريد مُفعَّل بالفعل؟ ─────────────────────
        if (user.EmailConfirmed)
        {
            _logger.LogInformation(
                "VerifyEmail: Email already confirmed for {UserId}",
                command.UserId);

            return VerifyEmailResult.Ok();
        }

        // ── 3. تأكد من وجود بريد إلكتروني ─────────────────────
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "VerifyEmail: No email set for user {UserId}",
                command.UserId);

            return VerifyEmailResult.Fail(
                "NO_EMAIL",
                "لا يوجد بريد إلكتروني مُضاف لهذا الحساب.");
        }

        // ── 4. التحقق من الرمز عبر Infrastructure Identity ─────
        var confirmResult = await _identityUsers.ConfirmEmailAsync(
            user,
            command.Token,
            ct);

        if (!confirmResult.Succeeded)
        {
            _logger.LogWarning(
                "VerifyEmail: Confirmation failed for {UserId}. Errors: {Errors}",
                command.UserId,
                string.Join(", ", confirmResult.Errors));

            return VerifyEmailResult.Fail(
                "INVALID_TOKEN",
                "رابط التحقق غير صالح أو منتهي الصلاحية. اطلب رابطاً جديداً.");
        }

        _logger.LogInformation(
            "VerifyEmail: Email {Email} confirmed for user {UserId}",
            user.Email,
            command.UserId);

        return VerifyEmailResult.Ok();
    }
}
