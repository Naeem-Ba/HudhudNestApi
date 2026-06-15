namespace PropertyApi.Architecture.Tests.ProductionHardening;

public sealed class SecurityTests
{
    [Fact(DisplayName = "Production DB connection must not trust invalid server certificates")]
    public void DependencyInjection_Should_Not_Use_TrustServerCertificate_In_ProductionResolver()
    {
        var source = ReadSource("PropertyApi.Infrastructure", "DependencyInjection.cs");

        Assert.Contains("SSL Mode=Require", source);
        Assert.DoesNotContain("SSL Mode=Require;Trust Server Certificate=true", source);
        Assert.Contains("Production database connection must not contain Trust Server Certificate=true", source);
    }

    [Fact(DisplayName = "Program must enable forwarded headers, HSTS, HTTPS, and RequireHttps in Production")]
    public void Program_Should_Enable_Production_Https_Hardening()
    {
        var source = ReadSource("PropertyApi", "Program.cs");

        Assert.Contains("UseForwardedHeaders", source);
        Assert.Contains("UseHsts", source);
        Assert.Contains("UseHttpsRedirection", source);
        Assert.Contains("RequireHttpsAttribute", source);
        Assert.Contains("IncludeSubDomains = true", source);
        Assert.Contains("Preload = true", source);
    }

    [Fact(DisplayName = "Production must validate SMS provider startup settings")]
    public void Infrastructure_Should_Validate_Sms_Settings_On_Start()
    {
        var dependencyInjection = ReadSource("PropertyApi.Infrastructure", "DependencyInjection.cs");
        var startupValidator = ReadSource("PropertyApi.Infrastructure", "Health", "ProductionStartupValidator.cs");
        var smsProviderOptions = ReadSource("PropertyApi.Infrastructure", "Auth", "Services", "SmsProviderOptions.cs");

        Assert.Contains("SmsProviderOptions", dependencyInjection);
        Assert.Contains("ValidateOnStart", dependencyInjection);
        Assert.Contains("ValidateForEnvironment", startupValidator);
        Assert.Contains("SmsProvider:ApiKey is required in Production", smsProviderOptions);
        Assert.Contains("SmsProvider:ApiUrl must be a valid absolute HTTPS URL in Production", smsProviderOptions);
        Assert.Contains("SmsProvider:ApiUrl must use HTTPS in Production", smsProviderOptions);
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
            if (File.Exists(Path.Combine(directory.FullName, "PropertyApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing PropertyApi.sln.");
    }
}
