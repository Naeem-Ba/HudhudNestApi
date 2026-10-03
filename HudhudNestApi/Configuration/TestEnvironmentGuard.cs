namespace HudhudNestApi.Configuration;

/// <summary>
/// Refuses to boot a real deployment whose environment name is <c>Testing</c> or <c>CI</c>.
///
/// Those two names are the test harness's switches: they expose exception messages and stack traces in
/// 500 responses, turn Swagger on, and fall back to an any-origin CORS policy when no origins are
/// configured. <see cref="ProductionEnvironmentGuard"/> only runs for <c>Production</c>, so a service
/// started with <c>ASPNETCORE_ENVIRONMENT=Testing</c> by mistake would quietly publish all of that
/// (security audit 2026-10-03, F-09). Render injects <c>RENDER</c> into every service it runs; its
/// presence is the signal that this is a hosted deployment and not a developer or CI process.
/// </summary>
public static class TestEnvironmentGuard
{
    private static readonly string[] HostedDeploymentMarkers =
    [
        "RENDER",
        "RENDER_SERVICE_ID",
        "RENDER_EXTERNAL_URL"
    ];

    public static void Validate(IHostEnvironment environment)
        => Validate(environment, Environment.GetEnvironmentVariable);

    public static void Validate(IHostEnvironment environment, Func<string, string?> getVariable)
    {
        var isTestHarnessEnvironment =
            environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase) ||
            environment.EnvironmentName.Equals("CI", StringComparison.OrdinalIgnoreCase);

        if (!isTestHarnessEnvironment)
            return;

        var marker = HostedDeploymentMarkers.FirstOrDefault(name => !string.IsNullOrWhiteSpace(getVariable(name)));
        if (marker is null)
            return;

        throw new InvalidOperationException(
            $"Environment '{environment.EnvironmentName}' is a test-harness environment (detailed errors, Swagger, " +
            $"permissive CORS) and must not run on a hosted deployment, but {marker} is set. " +
            "Set ASPNETCORE_ENVIRONMENT to Staging or Production.");
    }
}
