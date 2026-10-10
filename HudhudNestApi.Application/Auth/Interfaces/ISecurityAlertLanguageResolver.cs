namespace HudhudNestApi.Application.Auth.Interfaces;

/// <summary>
/// The language a security email to an account is written in: the language the ACCOUNT HOLDER chose,
/// found from the address the alert is going to -- never from the request that caused the alert
/// (anyone can provoke a failed-login alert for someone else's address).
/// </summary>
public interface ISecurityAlertLanguageResolver
{
    /// <summary>"ar" | "en" | "de", or null when the address has no account or its owner never chose a language.</summary>
    Task<string?> GetLanguageByEmailAsync(string email, CancellationToken ct = default);
}
