using System.Text.RegularExpressions;

namespace PropertyApi.Auth.Tests.Infrastructure;

[Trait("Category", "Security")]
[Trait("Feature", "OTP")]
public sealed class OtpGenerationTests
{
    private static OtpService CreateService()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager
        {
            ["OtpSettings:SecretKey"] = "TEST_ONLY_OTP_SECRET_KEY_1234567890_1234567890"
        };

        return new OtpService(config);
    }

    [Fact(DisplayName = "Generate returns a six-digit numeric OTP")]
    public void Generate_ShouldReturn_SixDigitNumber()
    {
        var service = CreateService();

        var (otp, hash) = service.Generate();

        Assert.Equal(6, otp.Length);
        Assert.Matches(new Regex("^\\d{6}$", RegexOptions.CultureInvariant), otp);
        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual(otp, hash);
    }

    [Fact(DisplayName = "Generate creates a hash that verifies only the matching OTP")]
    public void Generate_ShouldCreateHash_VerifiableOnlyByMatchingOtp()
    {
        var service = CreateService();

        var (otp, hash) = service.Generate();
        var wrongOtp = otp == "999999" ? "000000" : (int.Parse(otp) + 1).ToString("D6");

        Assert.True(service.Verify(otp, hash));
        Assert.False(service.Verify(wrongOtp, hash));
    }

    [Fact(DisplayName = "Generate produces varied OTP values across repeated calls")]
    public void Generate_ShouldProduce_VariedValuesAcrossRepeatedCalls()
    {
        var service = CreateService();
        var otps = new HashSet<string>();

        for (var i = 0; i < 100; i++)
        {
            var (otp, _) = service.Generate();
            Assert.Matches("^\\d{6}$", otp);
            otps.Add(otp);
        }

        Assert.True(
            otps.Count >= 95,
            $"Expected high OTP variety. Unique values: {otps.Count}/100.");
    }

    [Fact(DisplayName = "OtpService source uses RandomNumberGenerator, not System.Random")]
    public void Generate_ShouldUse_CryptographicRandomNumberGenerator()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "PropertyApi.Infrastructure",
            "Auth",
            "Services",
            "OtpService.cs");

        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RandomNumberGenerator", source);
        Assert.DoesNotContain("new Random(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Random.Shared", source, StringComparison.Ordinal);
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
