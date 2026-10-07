namespace HudhudNestApi.Infrastructure.Email;

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

    /// <summary>
    /// True for exactly one address with no display name. The sender composes
    /// "FromName &lt;From&gt;" itself, so a From that already carries a name or brackets
    /// would produce a malformed header that Resend rejects only at send time.
    /// </summary>
    public static bool IsBareEmailAddress(string value)
        => System.Net.Mail.MailAddress.TryCreate(value, out var address) &&
           string.Equals(address.Address, value, StringComparison.Ordinal);

    /// <summary>
    /// Fails fast in Production against the one deployment mistake startup validation
    /// otherwise cannot see: <see cref="From" /> left at its shipped placeholder domain.
    /// That address passes every other check -- it is non-empty, and nothing in this
    /// process can ask Resend whether a domain is verified -- yet Resend rejects it with a
    /// 403 at send time, and both callers of the send path swallow that failure by design
    /// (see docs/architecture/email-confirmation-and-delivery.md). Left unchecked, that
    /// combination means every registration succeeds and zero mail ever arrives.
    /// </summary>
    public void ValidateForEnvironment(string environmentName)
    {
        var isProduction = string.Equals(
            environmentName,
            "Production",
            StringComparison.OrdinalIgnoreCase);

        if (!isProduction)
        {
            return;
        }

        if (From.EndsWith("@hudhudnest.local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Email:From must not use the placeholder @hudhudnest.local domain in " +
                "Production. Set it to an address on a domain verified with the email " +
                "provider (e.g. Resend), or every send will be rejected with a silent 403.");
        }

        // A blank Provider means SMTP in Production (see ResolveEmailProvider), and the SMTP
        // sender only discovers a missing host when it tries to send -- an error the callers
        // swallow. Production ran like that: every registration succeeded, the confirmation
        // mail failed with "SMTP host is not configured", and nothing stopped the deployment.
        var resolvesToSmtp =
            string.IsNullOrWhiteSpace(Provider) ||
            string.Equals(Provider.Trim(), "Smtp", StringComparison.OrdinalIgnoreCase);

        if (resolvesToSmtp && string.IsNullOrWhiteSpace(SmtpHost))
        {
            throw new InvalidOperationException(
                "Email:Provider resolves to Smtp in Production but Email:SmtpHost is empty, so " +
                "no confirmation or password-reset mail could ever be sent. Set " +
                "Email:Provider=Resend with Email:Resend:ApiKey and Email:From, or configure " +
                "Email:SmtpHost.");
        }
    }
}
