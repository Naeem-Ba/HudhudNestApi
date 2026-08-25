namespace PropertyApi.Infrastructure.Email;

public sealed class ResendEmailOptions
{
    public const string SectionName = "Email:Resend";

    /// <summary>
    /// Never committed. Supplied by user-secrets locally and by the
    /// <c>Email__Resend__ApiKey</c> environment variable when deployed.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.resend.com";

    public int TimeoutSeconds { get; init; } = 10;
}
