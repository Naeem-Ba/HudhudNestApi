namespace PropertyApi.Security.Csrf;

public sealed class CookieCsrfOptions
{
    public const string SectionName = "CookieCsrf";

    /// <summary>
    /// Keep disabled while the API uses Authorization: Bearer tokens only.
    /// Enable this only when browser authentication cookies are introduced.
    /// </summary>
    public bool Enabled { get; set; } = false;

    public string AuthenticationCookieName { get; set; } = ".AspNetCore.Identity.Application";

    public bool ProtectAnonymousUnsafeEndpoints { get; set; } = false;
}
