using Xunit;

namespace HudhudNestApi.Architecture.Tests.ProductionHardening;

public sealed class SecurityTests
{
    [Fact(DisplayName = "Production DB connection must not trust invalid server certificates")]
    public void DependencyInjection_Should_Not_Use_TrustServerCertificate_In_ProductionResolver()
    {
        var source = ReadSource("HudhudNestApi.Infrastructure", "PostgresConnectionStringResolver.cs");

        Assert.Contains("SslMode.Require", source);
        Assert.DoesNotContain("SSL Mode=Require;Trust Server Certificate=true", source);
        Assert.Contains("Production database connection must not contain Trust Server Certificate=true", source);
    }

    [Fact(DisplayName = "Program must use proxy-aware HTTPS hardening behind Render")]
    public void Program_Must_Use_ProxyAware_HttpsHardening_Behind_Render()
    {
        var programPath = FindProgramCsPath();
        var source = File.ReadAllText(programPath);

        Assert.Contains("UseForwardedHeaders", source);
        Assert.Contains("UseHsts", source);

        // Render/Cloudflare terminates HTTPS before the request reaches Kestrel.
        // Therefore MVC RequireHttpsAttribute must not force internal redirects.
        Assert.DoesNotContain("RequireHttpsAttribute", source);

        // HTTPS redirection may remain for non-production environments,
        // but it should not be enforced inside the Production branch on Render.
        Assert.Contains("UseHttpsRedirection", source);
    }

    private static string FindProgramCsPath()
    {
        var current = Directory.GetCurrentDirectory();

        while (current is not null)
        {
            var candidate = Path.Combine(current, "HudhudNestApi", "Program.cs");

            if (File.Exists(candidate))
                return candidate;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException("Could not find HudhudNestApi/Program.cs.");
    }

    [Fact(DisplayName = "Production must validate SMS provider startup settings")]
    public void Infrastructure_Should_Validate_Sms_Settings_On_Start()
    {
        var dependencyInjection = ReadSource("HudhudNestApi.Infrastructure", "DependencyInjection.cs");
        var authInfrastructure = ReadSource("HudhudNestApi.Infrastructure", "Auth", "AuthInfrastructureRegistration.cs");
        var startupValidator = ReadSource("HudhudNestApi.Infrastructure", "Health", "ProductionStartupValidator.cs");
        var smsOptions = ReadSource("HudhudNestApi.Infrastructure", "Auth", "Services", "SmsProviderOptions.cs");
        var compositionSource =
            dependencyInjection + Environment.NewLine + authInfrastructure;

        Assert.Contains("AddAuthInfrastructure", dependencyInjection);
        Assert.Contains("SmsProviderOptions", compositionSource);
        Assert.Contains("ValidateOnStart", compositionSource);
        Assert.Contains("ValidateSmsSettings", startupValidator);
        Assert.Contains("ValidateForEnvironment", startupValidator);

        Assert.Contains("SmsProvider:ApiKey is required in Production", smsOptions);
        Assert.Contains("SmsProvider:ApiUrl must be a valid absolute HTTPS URL in Production", smsOptions);
        Assert.Contains("SmsProvider:ApiUrl must use HTTPS in Production", smsOptions);
    }

    private static string ReadSource(params string[] relativePath)
    {
        var repoRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { repoRoot }.Concat(relativePath).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HudhudNestApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing HudhudNestApi.sln.");
    }
}
