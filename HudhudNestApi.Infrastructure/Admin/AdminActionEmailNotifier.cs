using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Security;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Email;
using HudhudNestApi.Infrastructure.Identity.Entities;

namespace HudhudNestApi.Infrastructure.Admin;

/// <summary>
/// Email notice (ar/en/de, the reader's own language) for role changes, disabling and re-enabling of an
/// account: one mail to the account owner and one to each other active administrator.
/// </summary>
public sealed class AdminActionEmailNotifier : IAdminActionNotifier
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IEmailSender _email;
    private readonly ILogger<AdminActionEmailNotifier> _logger;
    private readonly ISecurityAlertLanguageResolver? _languages;

    public AdminActionEmailNotifier(
        UserManager<ApplicationUser> users,
        IEmailSender email,
        ILogger<AdminActionEmailNotifier> logger,
        ISecurityAlertLanguageResolver? languages = null)
    {
        _users = users;
        _email = email;
        _logger = logger;
        _languages = languages;
    }

    public async Task NotifyAsync(
        AdminAccountAction action, Guid targetUserId, Guid actorUserId, string? detail, CancellationToken ct = default)
    {
        try
        {
            var target = await _users.FindByIdAsync(targetUserId.ToString());
            var actor = await _users.FindByIdAsync(actorUserId.ToString());
            if (target is null) return;

            // The owner sees the actor's masked address; other administrators see full addresses.
            if (!string.IsNullOrWhiteSpace(target.Email) && target.Id != actorUserId)
                await SendAsync(target.Email!, action, forOwner: true, Label(actor, mask: true), Label(target, mask: true), detail, ct);

            var actorLabel = Label(actor, mask: false);
            var targetLabel = Label(target, mask: false);

            var admins = await _users.GetUsersInRoleAsync(RoleNames.Admin);
            foreach (var admin in admins
                         .Where(x => x.Id != actorUserId && x.Id != target.Id && !string.IsNullOrWhiteSpace(x.Email))
                         .DistinctBy(x => x.Id))
            {
                await SendAsync(admin.Email!, action, forOwner: false, actorLabel, targetLabel, detail, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The admin operation already succeeded and is in the audit log; a mail failure must not undo it.
            _logger.LogWarning(ex, "Admin action notice for {Action} could not be sent.", action);
        }
    }

    private async Task SendAsync(
        string to, AdminAccountAction action, bool forOwner,
        string actor, string target, string? detail, CancellationToken ct)
    {
        try
        {
            var lang = await LanguageAsync(to, ct);
            var (subject, body) = Compose(lang, action, forOwner, actor, target, detail);
            await _email.SendEmailAsync(to, subject, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Admin action notice to {Email} failed.", PiiMasking.MaskEmail(to));
        }
    }

    private async Task<string> LanguageAsync(string email, CancellationToken ct)
    {
        if (_languages is null) return SecurityAlertTemplates.LegacyLanguage;
        try { return SecurityAlertTemplates.Resolve(await _languages.GetLanguageByEmailAsync(email, ct)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return SecurityAlertTemplates.LegacyLanguage; }
    }

    private static string Label(ApplicationUser? user, bool mask)
    {
        if (user is null || string.IsNullOrWhiteSpace(user.Email)) return "?";
        return WebUtility.HtmlEncode(mask ? PiiMasking.MaskEmail(user.Email) : user.Email);
    }

    /// <summary>Subject and HTML-safe body. Addresses are HTML-encoded by <see cref="Label"/>; <paramref name="detail"/> is encoded here.</summary>
    public static (string Subject, string Body) Compose(
        string lang, AdminAccountAction action, bool forOwner, string actor, string target, string? detail)
    {
        var d = string.IsNullOrWhiteSpace(detail) ? null : WebUtility.HtmlEncode(detail.Trim());
        lang = SecurityAlertTemplates.Resolve(lang);
        var verb = (lang, action) switch
        {
            (SupportedPreferences.Arabic, AdminAccountAction.RoleChanged) => "تغيير صلاحيات",
            (SupportedPreferences.Arabic, AdminAccountAction.Disabled) => "تعطيل",
            (SupportedPreferences.Arabic, _) => "إعادة تفعيل",
            (SupportedPreferences.German, AdminAccountAction.RoleChanged) => "Änderung der Rollen",
            (SupportedPreferences.German, AdminAccountAction.Disabled) => "Deaktivierung",
            (SupportedPreferences.German, _) => "Reaktivierung",
            (_, AdminAccountAction.RoleChanged) => "role change",
            (_, AdminAccountAction.Disabled) => "disabling",
            _ => "re-enabling"
        };

        return lang switch
        {
            SupportedPreferences.Arabic => (
                forOwner ? $"تنبيه أمني: {verb} لحسابك" : $"تنبيه إداري: {verb} لحساب {target}",
                forOwner
                    ? $"قام مشرف ({actor}) بإجراء {verb} على حسابك في HudhudNest.{(d is null ? "" : $"<br>التفاصيل: {d}")}<br>إن لم تتوقع ذلك فتواصل مع الدعم فورًا."
                    : $"قام المشرف {actor} بإجراء {verb} على الحساب {target}.{(d is null ? "" : $"<br>التفاصيل: {d}")}<br>إن لم يكن هذا الإجراء مقصودًا فراجع سجل التدقيق فورًا."),
            SupportedPreferences.German => (
                forOwner ? $"Sicherheitshinweis: {verb} Ihres Kontos" : $"Admin-Hinweis: {verb} für {target}",
                forOwner
                    ? $"Ein Administrator ({actor}) hat bei Ihrem HudhudNest-Konto folgende Aktion ausgeführt: {verb}.{(d is null ? "" : $"<br>Details: {d}")}<br>Falls Sie das nicht erwartet haben, wenden Sie sich umgehend an den Support."
                    : $"Der Administrator {actor} hat für das Konto {target} ausgeführt: {verb}.{(d is null ? "" : $"<br>Details: {d}")}<br>Falls das nicht beabsichtigt war, prüfen Sie sofort das Audit-Protokoll."),
            _ => (
                forOwner ? $"Security notice: {verb} of your account" : $"Admin notice: {verb} for {target}",
                forOwner
                    ? $"An administrator ({actor}) performed a {verb} on your HudhudNest account.{(d is null ? "" : $"<br>Details: {d}")}<br>If you did not expect this, contact support immediately."
                    : $"Administrator {actor} performed a {verb} on account {target}.{(d is null ? "" : $"<br>Details: {d}")}<br>If this was not intended, review the audit log immediately.")
        };
    }
}
