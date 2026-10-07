using System.Net;
using System.Text;

namespace HudhudNestApi.Infrastructure.Email.Templates;

/// <summary>
/// The address-confirmation message, in the language the person was using on the site.
///
/// The callers (registration, add-email, resend) pass the site language the client was
/// showing. A tag we do not ship ("fr"), or none at all (an older client build that predates
/// the field), falls back to the original Arabic-and-English message: that is the honest
/// answer to "we do not know what this reader speaks", and it keeps those callers exactly
/// as they were instead of guessing a language for them.
///
/// The translatable-code convention in docs/password-policy.md governs API error payloads,
/// not message bodies, so the three languages live here, next to the markup they fill.
/// </summary>
public static class EmailConfirmationTemplate
{
    /// <summary>Subject of the bilingual fallback message.</summary>
    public const string Subject =
        "هدهد نيست · HudhudNest — أكِّد بريدك الإلكتروني · Confirm your email";

    private const string ButtonColor = "#1a73e8";

    /// <summary>
    /// Maps a client-supplied language tag to one of the shipped languages, or null. Accepts
    /// "ar", "en", "de" in any case and regional forms such as "ar-SA" or "en_US".
    /// </summary>
    public static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var primary = language.Trim()
            .Split('-', '_')[0]
            .ToLowerInvariant();

        return primary is "ar" or "en" or "de"
            ? primary
            : null;
    }

    public static string SubjectFor(string? language)
        => Find(language)?.Subject ?? Subject;

    public static string BuildHtml(string confirmationUrl, string? language = null)
    {
        // Encoded for an attribute, not just for text: the URL carries a token that is
        // base64url-ish and could otherwise close the quote.
        var href = WebUtility.HtmlEncode(confirmationUrl);
        var single = Find(language);

        var html = new StringBuilder();

        html.Append("<html");

        if (single is not null)
        {
            html.Append($" lang=\"{single.Code}\" dir=\"{single.Direction}\"");
        }

        html.Append(">\n<body style=\"margin:0;padding:24px;background:#f5f6f8;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1f2933\">\n");

        // Shown by mail clients next to the subject in the inbox list, hidden in the body.
        var preheader = single?.Preheader ?? Arabic.Preheader;
        html.Append($"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0\">{preheader}</div>\n");

        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;overflow:hidden\">\n");

        var brand = single is null
            ? "هدهد نيست · HudhudNest"
            : single.Brand;

        html.Append($"<tr><td align=\"center\" style=\"padding:20px 32px;background:#14213d;color:#ffffff;font-size:22px;font-weight:700;letter-spacing:.3px\">{brand}</td></tr>\n");

        if (single is not null)
        {
            AppendHtmlSection(html, single, href);
        }
        else
        {
            AppendHtmlSection(html, Arabic, href);
            html.Append("<tr><td style=\"padding:0 32px\"><hr style=\"border:none;border-top:1px solid #e4e7eb;margin:0\"></td></tr>\n");
            AppendHtmlSection(html, English, href);
        }

        html.Append("</table>\n</body>\n</html>\n");

        return html.ToString();
    }

    public static string BuildText(string confirmationUrl, string? language = null)
    {
        // Not stripped from the HTML above -- written for a reader, with the link on its
        // own line so no client mangles it when wrapping.
        var single = Find(language);

        if (single is not null)
        {
            return BuildTextSection(single, confirmationUrl);
        }

        return BuildTextSection(Arabic, confirmationUrl)
            + "\n\n--------------------------------------------------\n\n"
            + BuildTextSection(English, confirmationUrl);
    }

    private static void AppendHtmlSection(StringBuilder html, Copy copy, string href)
    {
        var align = copy.Direction == "rtl" ? "right" : "left";

        html.Append($"<tr><td style=\"padding:32px\" dir=\"{copy.Direction}\" lang=\"{copy.Code}\" align=\"{align}\">\n");
        html.Append($"<h2 style=\"margin:0 0 16px;font-size:20px\">{copy.Heading}</h2>\n");
        html.Append($"<p style=\"margin:0 0 12px;line-height:1.7;font-weight:600\">{copy.Greeting}</p>\n");
        html.Append($"<p style=\"margin:0 0 24px;line-height:1.7\">{copy.Intro}</p>\n");
        html.Append($"<p style=\"margin:0 0 24px\"><a href=\"{href}\" style=\"display:inline-block;padding:12px 28px;background:{ButtonColor};color:#ffffff;text-decoration:none;border-radius:6px;font-weight:600\">{copy.Button}</a></p>\n");
        html.Append($"<p style=\"margin:0 0 8px;line-height:1.7;color:#5c6773;font-size:13px\">{copy.Fallback}</p>\n");
        html.Append($"<p style=\"margin:0 0 16px;word-break:break-all;font-size:12px;color:#5c6773;direction:ltr;text-align:left\">{href}</p>\n");
        html.Append($"<p style=\"margin:0 0 16px;line-height:1.7;color:#5c6773;font-size:13px\">{copy.Safety}</p>\n");
        html.Append($"<p style=\"margin:0;line-height:1.7;color:#8a94a0;font-size:12px\">{copy.Footer}</p>\n");
        html.Append("</td></tr>\n");
    }

    private static string BuildTextSection(Copy copy, string confirmationUrl)
        => string.Join(
            "\n",
            copy.Heading,
            string.Empty,
            copy.Greeting,
            copy.Intro,
            string.Empty,
            confirmationUrl,
            string.Empty,
            copy.Safety,
            string.Empty,
            copy.Footer);

    private static Copy? Find(string? language)
        => NormalizeLanguage(language) switch
        {
            "ar" => Arabic,
            "en" => English,
            "de" => German,
            _ => null
        };

    private sealed record Copy(
        string Code,
        string Direction,
        string Brand,
        string Subject,
        string Preheader,
        string Heading,
        string Greeting,
        string Intro,
        string Button,
        string Fallback,
        string Safety,
        string Footer);

    private static readonly Copy Arabic = new(
        Code: "ar",
        Direction: "rtl",
        Brand: "هدهد نيست",
        Subject: "أكِّد بريدك الإلكتروني لتفعيل حسابك في هدهد نيست",
        Preheader: "خطوة واحدة تفصلك عن تفعيل حسابك في هدهد نيست.",
        Heading: "أكِّد بريدك الإلكتروني",
        Greeting: "مرحباً بك في هدهد نيست،",
        Intro: "شكراً لانضمامك إلى منصة هدهد نيست. لتفعيل حسابك، يُرجى تأكيد عنوان بريدك الإلكتروني بالضغط على الزر أدناه.",
        Button: "تأكيد البريد الإلكتروني",
        Fallback: "إن لم يعمل الزر، انسخ الرابط التالي والصقه في متصفحك:",
        Safety: "إن لم تنشئ حساباً في هدهد نيست فتجاهل هذه الرسالة ولن يتغيّر شيء. ولا يطلب فريق هدهد نيست كلمة مرورك أبداً عبر البريد الإلكتروني.",
        Footer: "هذه رسالة آلية من هدهد نيست، يُرجى عدم الرد عليها.");

    private static readonly Copy English = new(
        Code: "en",
        Direction: "ltr",
        Brand: "HudhudNest",
        Subject: "Confirm your email to activate your HudhudNest account",
        Preheader: "One step left to activate your HudhudNest account.",
        Heading: "Confirm your email address",
        Greeting: "Welcome to HudhudNest,",
        Intro: "Thank you for joining HudhudNest. To activate your account, please confirm your email address using the button below.",
        Button: "Confirm email address",
        Fallback: "If the button does not work, copy this link into your browser:",
        Safety: "If you did not create a HudhudNest account, you can safely ignore this email and nothing will change. HudhudNest will never ask for your password by email.",
        Footer: "This is an automated message from HudhudNest. Please do not reply to it.");

    private static readonly Copy German = new(
        Code: "de",
        Direction: "ltr",
        Brand: "HudhudNest",
        Subject: "Bestätigen Sie Ihre E-Mail-Adresse, um Ihr HudhudNest-Konto zu aktivieren",
        Preheader: "Nur noch ein Schritt bis zur Aktivierung Ihres HudhudNest-Kontos.",
        Heading: "E-Mail-Adresse bestätigen",
        Greeting: "Willkommen bei HudhudNest!",
        Intro: "Vielen Dank für Ihre Registrierung bei HudhudNest. Um Ihr Konto zu aktivieren, bestätigen Sie bitte Ihre E-Mail-Adresse über die Schaltfläche unten.",
        Button: "E-Mail-Adresse bestätigen",
        Fallback: "Falls die Schaltfläche nicht funktioniert, kopieren Sie diesen Link in Ihren Browser:",
        Safety: "Wenn Sie kein HudhudNest-Konto erstellt haben, können Sie diese E-Mail ignorieren – es ändert sich nichts. HudhudNest fragt Sie niemals per E-Mail nach Ihrem Passwort.",
        Footer: "Dies ist eine automatische Nachricht von HudhudNest. Bitte antworten Sie nicht darauf.");
}
