using System.Net;

namespace PropertyApi.Infrastructure.Email.Templates;

/// <summary>
/// The address-confirmation message, in Arabic and English in the same email.
///
/// One message rather than two localised variants because nothing upstream knows which
/// language to pick: the two callers -- registration and add-email -- carry no language
/// preference, and the solution has no localisation infrastructure for email at all
/// (the translatable-code convention in docs/password-policy.md governs API error
/// payloads, not message bodies). A bilingual email is the honest answer to "we do not
/// know what this reader speaks" without inventing a mechanism to carry that answer.
/// </summary>
public static class EmailConfirmationTemplate
{
    public const string Subject =
        "تأكيد بريدك الإلكتروني · Confirm your email";

    public static string BuildHtml(string confirmationUrl)
    {
        // Encoded for an attribute, not just for text: the URL carries a token that is
        // base64url-ish and could otherwise close the quote.
        var href = WebUtility.HtmlEncode(confirmationUrl);

        return $"""
            <html>
            <body style="margin:0;padding:24px;background:#f5f6f8;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1f2933">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px">
                <tr>
                  <td style="padding:32px" dir="rtl" lang="ar" align="right">
                    <h2 style="margin:0 0 16px;font-size:20px">تأكيد بريدك الإلكتروني</h2>
                    <p style="margin:0 0 16px;line-height:1.7">مرحباً،</p>
                    <p style="margin:0 0 24px;line-height:1.7">لتفعيل حسابك، اضغط الزر التالي لتأكيد عنوان بريدك الإلكتروني.</p>
                    <p style="margin:0 0 24px">
                      <a href="{href}" style="display:inline-block;padding:12px 28px;background:#1a73e8;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:600">تأكيد البريد الإلكتروني</a>
                    </p>
                    <p style="margin:0;line-height:1.7;color:#5c6773;font-size:13px">إذا لم تطلب هذا، يمكنك تجاهل هذه الرسالة بأمان.</p>
                  </td>
                </tr>
                <tr>
                  <td style="padding:0 32px"><hr style="border:none;border-top:1px solid #e4e7eb;margin:0"></td>
                </tr>
                <tr>
                  <td style="padding:32px" dir="ltr" lang="en" align="left">
                    <h2 style="margin:0 0 16px;font-size:20px">Confirm your email</h2>
                    <p style="margin:0 0 16px;line-height:1.7">Hello,</p>
                    <p style="margin:0 0 24px;line-height:1.7">To activate your account, confirm your email address using the button below.</p>
                    <p style="margin:0 0 24px">
                      <a href="{href}" style="display:inline-block;padding:12px 28px;background:#1a73e8;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:600">Confirm email</a>
                    </p>
                    <p style="margin:0 0 8px;line-height:1.7;color:#5c6773;font-size:13px">If the button does not work, copy this link into your browser:</p>
                    <p style="margin:0 0 16px;word-break:break-all;font-size:12px;color:#5c6773">{href}</p>
                    <p style="margin:0;line-height:1.7;color:#5c6773;font-size:13px">If you did not request this, you can safely ignore this email.</p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    public static string BuildText(string confirmationUrl)
    {
        // Not stripped from the HTML above -- written for a reader, with the link on its
        // own line so no client mangles it when wrapping.
        return $"""
            تأكيد بريدك الإلكتروني

            مرحباً،
            لتفعيل حسابك، افتح الرابط التالي لتأكيد عنوان بريدك الإلكتروني:

            {confirmationUrl}

            إذا لم تطلب هذا، يمكنك تجاهل هذه الرسالة بأمان.

            --------------------------------------------------

            Confirm your email

            Hello,
            To activate your account, open the following link to confirm your email address:

            {confirmationUrl}

            If you did not request this, you can safely ignore this email.
            """;
    }
}
