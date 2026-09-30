using System.Security.Cryptography;

namespace HudhudNestApi.Integration.Tests.TestInfrastructure;

/// <summary>
/// Provides process-wide security configuration for integration tests.
///
/// CI-provided environment variables take precedence. When tests run locally
/// without those variables, cryptographically random values are generated once
/// and reused for the lifetime of the test process.
/// </summary>
internal static class TestSecuritySettings
{
    public const string JwtIssuer = "HudhudNestApi";
    public const string JwtAudience = "HudhudNestClient";

    public static string JwtKey { get; } =
        GetOrCreateEnvironmentValue(
            "Jwt__Key",
            byteCount: 48);

    public static string OtpSecretKey { get; } =
        GetOrCreateEnvironmentValue(
            "OtpSettings__SecretKey",
            byteCount: 48);

    public static string PhoneLookupHmacKey { get; } =
        GetOrCreateEnvironmentValue(
            "Security__PhoneLookupHmacKey",
            byteCount: 32);

    public static void EnsureEnvironmentConfigured()
    {
        _ = JwtKey;
        _ = OtpSecretKey;
        _ = PhoneLookupHmacKey;
    }

    private static string GetOrCreateEnvironmentValue(
        string variableName,
        int byteCount)
    {
        var existingValue =
            Environment.GetEnvironmentVariable(variableName);

        if (!string.IsNullOrWhiteSpace(existingValue))
        {
            return existingValue;
        }

        var generatedValue = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(byteCount));

        Environment.SetEnvironmentVariable(
            variableName,
            generatedValue);

        return generatedValue;
    }
}
