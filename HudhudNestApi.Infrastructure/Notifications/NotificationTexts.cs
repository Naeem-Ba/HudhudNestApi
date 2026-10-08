using HudhudNestApi.Application.Notifications.Models;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Email.Templates;

namespace HudhudNestApi.Infrastructure.Notifications;

/// <summary>
/// The text of message-based notifications in the three site languages (ar | en | de). Notification
/// rows store one Arabic sentence when they are created, because the recipient's site language is
/// not known then (the sender is the one on the site). The text is therefore re-rendered when the
/// recipient reads their notifications, in the language of the site they are using. The message
/// content itself is never part of these texts.
/// </summary>
public static class NotificationTexts
{
    /// <summary>
    /// The localized text for <paramref name="type"/>, or null when the type has no localized
    /// template or the language is not one of the shipped ones (the caller keeps the stored text).
    /// </summary>
    public static string? Render(NotificationType type, string? language, NotificationMessageContext context)
    {
        var lang = EmailConfirmationTemplate.NormalizeLanguage(language);
        if (lang is null)
            return null;

        return type switch
        {
            NotificationType.NewMessage => lang switch
            {
                "ar" => $"أرسل لك {Name(context.SenderName, "مستخدم")} رسالة جديدة.",
                "de" => $"{Name(context.SenderName, "Ein Nutzer")} hat Ihnen eine neue Nachricht gesendet.",
                _ => $"{Name(context.SenderName, "A user")} sent you a new message.",
            },
            NotificationType.AgencyMemberInquiry => lang switch
            {
                "ar" => $"استفسار جديد: أرسل {Name(context.SenderName, "مستخدم")} رسالة إلى {Name(context.ReceiverName, "عضو المكتب")} بخصوص '{Name(context.PropertyTitle, "عقار")}' (مكتب '{Name(context.AgencyName, "مكتب عقاري")}').",
                "de" => $"Neue Anfrage: {Name(context.SenderName, "Ein Nutzer")} hat {Name(context.ReceiverName, "dem Büromitglied")} eine Nachricht zu '{Name(context.PropertyTitle, "einer Immobilie")}' gesendet (Büro '{Name(context.AgencyName, "ein Büro")}').",
                _ => $"New inquiry: {Name(context.SenderName, "A user")} sent a message to {Name(context.ReceiverName, "the agency member")} about '{Name(context.PropertyTitle, "a property")}' (agency '{Name(context.AgencyName, "an agency")}').",
            },
            _ => null,
        };
    }

    private static string Name(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
