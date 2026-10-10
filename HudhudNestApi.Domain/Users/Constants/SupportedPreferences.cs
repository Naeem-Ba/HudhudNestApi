namespace HudhudNestApi.Domain.Users.Constants;

/// <summary>
/// The single list of interface languages and display currencies an account may choose.
/// The validator, the entity and the notification localizer all read it, so a code the product
/// does not translate (or price) can never be stored and later fail somewhere far from the cause.
/// Codes are stored lower-case (languages) and upper-case (currencies).
/// </summary>
public static class SupportedPreferences
{
    public const string Arabic = "ar";
    public const string English = "en";
    public const string German = "de";

    /// <summary>
    /// What a recipient is addressed in when no explicit choice is on file. Arabic is the app's
    /// long-standing language; every account created before preferences were synced carries the
    /// column default ("en") without ever having chosen it, so that value must not be trusted
    /// until <see cref="Entities.UserAccount.PreferredLanguageSetAt"/> says the user set it.
    /// </summary>
    public const string FallbackLanguage = Arabic;

    public static readonly IReadOnlyList<string> Languages =
        new[] { Arabic, English, German };

    public static readonly IReadOnlyList<string> Currencies =
        new[] { "EUR", "USD", "SYP", "TRY", "AED", "SAR" };

    public static bool IsSupportedLanguage(string? code) =>
        code is not null && Languages.Contains(code.Trim().ToLowerInvariant());

    public static bool IsSupportedCurrency(string? code) =>
        code is not null && Currencies.Contains(code.Trim().ToUpperInvariant());

    /// <summary>"ar-SY" / "AR" / " de " -> "ar" / "ar" / "de"; null when the product has no translation for it.</summary>
    public static string? NormalizeLanguage(string? tag)
    {
        var primary = (tag ?? string.Empty).Trim().ToLowerInvariant().Split('-', '_')[0];
        return Languages.Contains(primary) ? primary : null;
    }
}
