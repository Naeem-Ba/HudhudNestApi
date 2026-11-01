using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Application.Auth.Phone;

/// <summary>
/// The language the one-time-code message is written in, for the current request. The client sends the
/// site language with the send-otp call; the controller stores it here and the delivery providers read it.
/// Scoped: one value per request. Null means "not told", which keeps the original Arabic wording.
/// </summary>
public interface IOtpMessageLanguage
{
    string? Value { get; set; }
}

public sealed class OtpMessageLanguage : IOtpMessageLanguage
{
    private string? _value;

    public string? Value
    {
        get => _value;
        set => _value = SupportedPreferences.NormalizeLanguage(value);
    }
}

/// <summary>The SMS text of a one-time code, naming the product so the reader knows who is asking.</summary>
public static class OtpMessageText
{
    public static string Sms(string? language, string otp) =>
        SupportedPreferences.NormalizeLanguage(language) switch
        {
            SupportedPreferences.English => $"HudhudNest verification code: {otp} (valid for 5 minutes)",
            SupportedPreferences.German => $"HudhudNest Bestätigungscode: {otp} (5 Minuten gültig)",
            // The original wording, kept for Arabic and for a client that did not say.
            _ => $"رمز التحقق: {otp} — صالح 5 دقائق",
        };
}
