namespace HudhudNestApi.Application.Notifications.Interfaces;

/// <summary>
/// The language a notification to a given user is written in. Always the RECIPIENT's: the person
/// whose action caused the event never influences it, and neither does anything the client sends.
/// </summary>
public interface IRecipientLanguageResolver
{
    /// <summary>
    /// "ar" | "en" | "de". The language the account holder explicitly chose; for an account that
    /// never chose one (or that cannot be found) the product fallback, so existing users keep
    /// receiving exactly what they received before preferences were synced.
    /// </summary>
    Task<string> GetLanguageAsync(Guid userId, CancellationToken ct = default);
}
