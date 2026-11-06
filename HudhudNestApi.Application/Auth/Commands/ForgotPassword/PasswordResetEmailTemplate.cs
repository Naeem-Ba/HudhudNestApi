using System.Net;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// The password-reset message in the reader's language (ar / en / de). An unknown or missing
/// language receives Arabic, the product's default display language. The product is called HudhudNest (the message
/// used to say "HudhudNestApi", the repository name).
/// </summary>
public static class PasswordResetEmailTemplate
{
    public static string Resolve(string? language) =>
        SupportedPreferences.NormalizeLanguage(language) ?? SupportedPreferences.FallbackLanguage;

    public static string SubjectFor(string? language) => Resolve(language) switch
    {
        SupportedPreferences.Arabic => "إعادة تعيين كلمة المرور في هدهد نيست",
        SupportedPreferences.German => "HudhudNest: Passwort zurücksetzen",
        _ => "Reset your password",
    };

    public static string BuildHtml(string? language, string? displayName, string resetUrl)
    {
        var lang = Resolve(language);
        var name = string.IsNullOrWhiteSpace(displayName) ? null : WebUtility.HtmlEncode(displayName.Trim());
        var href = WebUtility.HtmlEncode(resetUrl);

        var (dir, align, greeting, intro, link, ignore) = lang switch
        {
            SupportedPreferences.Arabic => (
                "rtl",
                "right",
                name is null ? "مرحبًا،" : $"مرحبًا {name}،",
                "وصلنا طلب لإعادة تعيين كلمة المرور لحسابك في هدهد نيست.",
                "إعادة تعيين كلمة المرور",
                "إن لم تطلب ذلك فتجاهل هذه الرسالة، ولن يتغيّر شيء."),
            SupportedPreferences.German => (
                "ltr",
                "left",
                name is null ? "Guten Tag," : $"Guten Tag {name},",
                "Wir haben eine Anfrage erhalten, das Passwort Ihres HudhudNest-Kontos zurückzusetzen.",
                "Passwort zurücksetzen",
                "Wenn Sie das nicht angefordert haben, können Sie diese E-Mail ignorieren – es ändert sich nichts."),
            _ => (
                "ltr",
                "left",
                name is null ? "Hello there," : $"Hello {name},",
                "We received a request to reset your HudhudNest password.",
                "Reset your password",
                "If you did not request this, you can safely ignore this email."),
        };

        return $"""
            <html lang="{lang}" dir="{dir}">
            <body style="font-family:Arial,sans-serif;line-height:1.6;text-align:{align}" dir="{dir}">
                <p>{greeting}</p>
                <p>{intro}</p>
                <p><a href="{href}">{link}</a></p>
                <p>{ignore}</p>
            </body>
            </html>
            """;
    }
}
