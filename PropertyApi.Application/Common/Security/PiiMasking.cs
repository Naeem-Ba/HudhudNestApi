namespace PropertyApi.Application.Common.Security;

/// <summary>
/// Masks personal data (email, phone, IP address) before it is interpolated into an
/// <c>ILogger</c> message.
///
/// <para>
/// The API project's <c>TelemetryRedactionProcessor</c> strips raw email/phone/IP/etc.
/// from exported OpenTelemetry <em>trace spans</em>, but that processor has no effect
/// on plain <c>ILogger</c> log lines — this codebase has no
/// Serilog/OTel-logs pipeline, so <c>ILogger</c> output goes straight to the console
/// (and whatever the hosting platform captures from stdout) unredacted. Call sites that
/// would otherwise write a raw email/phone/IP into a log message must mask it with one
/// of these helpers first. See <c>docs/privacy/privacy-gaps.md</c> (P0) for the audit
/// finding this fixes.
/// </para>
/// <para>
/// Masking, not omission, is deliberate: keeping a stable partial value (domain of an
/// email, last two digits of a phone, the IP's /24) still lets an operator correlate
/// repeated log lines about the same account/host while investigating an incident,
/// without the full value ever reaching a log sink.
/// </para>
/// </summary>
public static class PiiMasking
{
    /// <summary>Masks the local part of an email, keeping only its first character and domain. "jo***@example.com" → "j**@example.com".</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "(none)";

        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1)
            return "***";

        var local = email[..at];
        var domain = email[(at + 1)..];

        var maskedLocal = local.Length <= 1
            ? "*"
            : local[0] + new string('*', Math.Min(local.Length - 1, 6));

        return $"{maskedLocal}@{domain}";
    }

    /// <summary>Masks a phone number, keeping only its last two digits. "+491701234567" → "***67".</summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "(none)";

        return phone.Length <= 2
            ? "***"
            : "***" + phone[^2..];
    }

    /// <summary>Masks an IP address to its /24 (IPv4) or first two groups (IPv6). "203.0.113.42" → "203.0.113.0".</summary>
    public static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return "(none)";

        if (ip.Contains('.') && !ip.Contains(':'))
        {
            var parts = ip.Split('.');
            return parts.Length == 4
                ? $"{parts[0]}.{parts[1]}.{parts[2]}.0"
                : "***";
        }

        if (ip.Contains(':'))
        {
            var groups = ip.Split(':');
            return groups.Length >= 2
                ? $"{groups[0]}:{groups[1]}::"
                : "***";
        }

        return "***";
    }

    private static readonly System.Text.RegularExpressions.Regex Ipv4Pattern =
        new(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Masks every IPv4 address embedded in an arbitrary string (e.g. a composite
    /// rate-limit partition key like "user:abc123:ip:203.0.113.42") to its /24,
    /// leaving the rest of the string untouched.
    /// </summary>
    public static string MaskIpsInText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "(none)";

        return Ipv4Pattern.Replace(text, m => MaskIp(m.Value));
    }
}
