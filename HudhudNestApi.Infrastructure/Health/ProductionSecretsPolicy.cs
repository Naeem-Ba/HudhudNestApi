using Microsoft.Extensions.Configuration;

namespace HudhudNestApi.Infrastructure.Health;

/// <summary>
/// Production startup check for the three secrets that sign and hash everything the API
/// issues: <c>Jwt:Key</c>, <c>OtpSettings:SecretKey</c> and <c>Security:PhoneLookupHmacKey</c>.
///
/// Why this exists: the OTP and phone-lookup secrets were only read when the first request
/// needed them (<c>OtpService</c> and <c>HmacPhoneNumberLookupHasher</c> read them in their
/// constructors), so a Production service started without either one answered
/// <c>/health/ready</c> with 200 and then returned 500 for every phone registration and login.
/// And nothing stopped a service from running with the public CI constants
/// (<c>TEST_ONLY_…</c> in ci.yml / appsettings.Testing.json), which anyone can read in the repository.
///
/// The check never puts a secret value in a message -- only the setting name and the rule it broke.
/// </summary>
public static class ProductionSecretsPolicy
{
    public const string JwtKeySetting = "Jwt:Key";
    public const string OtpSecretSetting = "OtpSettings:SecretKey";
    public const string PhoneLookupHmacKeySetting = "Security:PhoneLookupHmacKey";

    /// <summary>Same floor the runtime enforces (JwtOptions, OtpService, HmacPhoneNumberLookupHasher).</summary>
    private const int MinimumTextSecretLength = 32;
    private const int MinimumHmacKeyBytes = 32;

    /// <summary>A random secret of this length has far more distinct symbols than this; "aaaa…" or "abab…" does not.</summary>
    private const int MinimumDistinctSymbols = 8;

    /// <summary>
    /// The HMAC key used by ci.yml and the test factories: the bytes 0..31 in Base64. It is public,
    /// so it is never a valid Production key. Built from the bytes instead of written out so a
    /// secret scanner has no key-shaped literal to flag.
    /// </summary>
    private static readonly string PublicCiPhoneLookupHmacKey =
        Convert.ToBase64String(Enumerable.Range(0, MinimumHmacKeyBytes).Select(i => (byte)i).ToArray());

    private static readonly string[] PlaceholderMarkers =
    [
        "TEST_ONLY",
        "CHANGE_ME",
        "CHANGEME",
        "REPLACE_ME",
        "PLACEHOLDER",
        "YOUR_",
        "YOUR-"
    ];

    /// <summary>Throws when any problem is found; the message lists every problem at once.</summary>
    public static void EnsureValid(IConfiguration configuration)
    {
        var problems = FindProblems(configuration);
        if (problems.Count == 0)
            return;

        throw new InvalidOperationException(
            "Production refuses to start with unusable secrets: " +
            string.Join(" ", problems) +
            " Set strong, unique values as environment variables on the Production service " +
            "(for example `openssl rand -base64 48` for the two text keys and " +
            "`openssl rand -base64 32` for the phone lookup key).");
    }

    public static IReadOnlyList<string> FindProblems(IConfiguration configuration)
    {
        var problems = new List<string>();

        var jwtKey = configuration[JwtKeySetting];
        var otpSecret = configuration[OtpSecretSetting];
        var hmacKey = configuration[PhoneLookupHmacKeySetting];

        CheckTextSecret(JwtKeySetting, jwtKey, problems);
        CheckTextSecret(OtpSecretSetting, otpSecret, problems);
        CheckHmacKey(hmacKey, problems);

        // One leaked or rotated secret must not take the others with it.
        CheckDistinct(JwtKeySetting, jwtKey, OtpSecretSetting, otpSecret, problems);
        CheckDistinct(JwtKeySetting, jwtKey, PhoneLookupHmacKeySetting, hmacKey, problems);
        CheckDistinct(OtpSecretSetting, otpSecret, PhoneLookupHmacKeySetting, hmacKey, problems);

        return problems;
    }

    private static void CheckTextSecret(string name, string? value, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{name} is not set.");
            return;
        }

        if (value.Length < MinimumTextSecretLength)
            problems.Add($"{name} must contain at least {MinimumTextSecretLength} characters.");

        var marker = FindPlaceholderMarker(value);
        if (marker is not null)
            problems.Add($"{name} looks like a placeholder or test value (it contains '{marker}').");

        if (value.Distinct().Count() < MinimumDistinctSymbols)
            problems.Add($"{name} has too little variety to be a random secret.");
    }

    private static void CheckHmacKey(string? value, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{PhoneLookupHmacKeySetting} is not set.");
            return;
        }

        if (string.Equals(value.Trim(), PublicCiPhoneLookupHmacKey, StringComparison.Ordinal))
            problems.Add($"{PhoneLookupHmacKeySetting} is the public CI test value.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            problems.Add($"{PhoneLookupHmacKeySetting} must be a valid Base64 value.");
            return;
        }

        if (bytes.Length < MinimumHmacKeyBytes)
            problems.Add($"{PhoneLookupHmacKeySetting} must contain at least {MinimumHmacKeyBytes} bytes.");

        if (bytes.Distinct().Count() < MinimumDistinctSymbols)
            problems.Add($"{PhoneLookupHmacKeySetting} has too little variety to be a random secret.");
    }

    private static void CheckDistinct(
        string firstName,
        string? first,
        string secondName,
        string? second,
        List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return;

        if (string.Equals(first.Trim(), second.Trim(), StringComparison.Ordinal))
            problems.Add($"{firstName} and {secondName} must be different values.");
    }

    private static string? FindPlaceholderMarker(string value)
    {
        foreach (var marker in PlaceholderMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return marker;
        }

        return null;
    }
}
