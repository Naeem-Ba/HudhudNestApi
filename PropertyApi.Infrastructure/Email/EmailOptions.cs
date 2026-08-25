namespace PropertyApi.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Which sender to compose: <c>Console</c>, <c>Smtp</c> or <c>Resend</c>.
    ///
    /// Left blank, registration falls back to the older rule -- SMTP in Production,
    /// console logging everywhere else -- so an environment that has not been told
    /// about this setting keeps behaving as it did.
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    /// <summary>Display name shown beside <see cref="From" />. Optional.</summary>
    public string FromName { get; init; } = string.Empty;

    public string SmtpHost { get; init; } = string.Empty;

    public int SmtpPort { get; init; } = 587;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool EnableSsl { get; init; } = true;
}
