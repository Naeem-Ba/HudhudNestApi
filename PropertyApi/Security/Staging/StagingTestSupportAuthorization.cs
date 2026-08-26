using System.Security.Cryptography;
using System.Text;

namespace PropertyApi.Security.Staging;

/// <summary>
/// Shared secret-header gate for anonymous endpoints that exist only for Staging automation —
/// StagingTestSupportController's cleanup call, and RELEASE-BLOCKERS-AR.md B-8's build-info.
/// Neither carries a JWT (the smoke test's technical-health check runs before any user
/// exists; the release-gate workflow polls from a CI runner with no user identity at all), so
/// the gate is a constant-time secret compare instead of [Authorize].
///
/// Extracted so the two controllers cannot drift on what "authorized" means — before this,
/// build-info had no gate at all while the cleanup endpoint already had this exact check.
/// </summary>
public static class StagingTestSupportAuthorization
{
    public const string SecretHeaderName = "X-Staging-Smoke-Secret";

    public static bool IsAuthorized(
        HttpRequest request,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsStaging() ||
            !configuration.GetValue<bool>("Staging:TestSupport:Enabled"))
            return false;

        // Reuses the same configured secret as the cleanup endpoint rather than adding a
        // second one: both are "only Staging automation should ever call this", and a real
        // deployment already has this value provisioned.
        var configured = configuration["Staging:TestSupport:CleanupSecret"];
        var supplied = request.Headers[SecretHeaderName].ToString();
        if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(supplied))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }
}
