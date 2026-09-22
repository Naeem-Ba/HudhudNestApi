namespace HudhudNestApi.Security.Headers;

public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    public bool Enabled { get; set; } = true;

    public string ContentSecurityPolicy { get; set; }
        = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
}
