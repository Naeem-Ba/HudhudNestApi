namespace HudhudNestApi.Controllers;

// Shared by every Staging-only, shared-secret-gated observability test endpoint
// (ObservabilitySyntheticController, ObservabilityAlertTestController). Kept dependency-free
// (just IConfiguration/IHostEnvironment) on purpose: controllers using it must not be forced
// to also depend on services -- e.g. IConnectionMultiplexer -- that aren't registered unless
// unrelated feature flags happen to be on for the current environment.
internal static class ObservabilityTestAuthorization
{
    public static bool IsAuthorized(
        HttpRequest request,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsStaging() ||
            !configuration.GetValue<bool>("Staging:TestSupport:Enabled"))
        {
            return false;
        }

        var expectedKey = configuration["Staging:TestSupport:CleanupSecret"];
        var suppliedKey = request.Headers["X-Observability-Test-Key"].ToString();
        return !string.IsNullOrWhiteSpace(expectedKey) &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expectedKey),
                System.Text.Encoding.UTF8.GetBytes(suppliedKey));
    }
}
