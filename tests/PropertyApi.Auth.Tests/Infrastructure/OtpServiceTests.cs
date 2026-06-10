using Microsoft.Extensions.Configuration;

namespace PropertyApi.Auth.Tests.Infrastructure;

[Trait("Category", "Infrastructure")]
[Trait("Service", "OtpService")]
public sealed class OtpServiceTests
{
    private static OtpService CreateService(string? secretKey = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OtpSettings:SecretKey"] = secretKey ?? "this-is-a-test-secret-key-minimum-32-chars!"
            })
            .Build();

        return new OtpService(config);
    }

    [Fact(DisplayName = "Generate: returns exactly six digits")]
    public void Generate_Returns_SixDigitOtp()
    {
        var service = CreateService();
        var (otp, _) = service.Generate();

        Assert.Equal(6, otp.Length);
        Assert.Matches(@"^\d{6}$", otp);
    }

    [Fact(DisplayName = "Generate: hash is not empty and differs from OTP")]
    public void Generate_HashIsNotEmpty_AndDiffersFromOtp()
    {
        var service = CreateService();
        var (otp, hash) = service.Generate();

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual(otp, hash);
    }

    [Fact(DisplayName = "Generate: multiple calls produce varied OTP values")]
    public void Generate_MultipleCalls_ProducesDifferentOtps()
    {
        var service = CreateService();
        var otps = new HashSet<string>();

        for (var i = 0; i < 10; i++)
        {
            var (otp, _) = service.Generate();
            otps.Add(otp);
        }

        Assert.True(otps.Count > 1, "All generated OTP values were identical.");
    }

    [Fact(DisplayName = "Generate: always returns value within 000000-999999 range")]
    public void Generate_AlwaysInValidRange()
    {
        var service = CreateService();

        for (var i = 0; i < 100; i++)
        {
            var (otp, _) = service.Generate();
            var number = int.Parse(otp);

            Assert.Equal(6, otp.Length);
            Assert.InRange(number, 0, 999_999);
        }
    }

    [Fact(DisplayName = "Verify: correct OTP with its hash returns true")]
    public void Verify_CorrectOtp_WithItsHash_ReturnsTrue()
    {
        var service = CreateService();
        var (otp, hash) = service.Generate();

        Assert.True(service.Verify(otp, hash));
    }

    [Fact(DisplayName = "Verify: wrong OTP returns false")]
    public void Verify_WrongOtp_ReturnsFalse()
    {
        var service = CreateService();
        var (otp1, _) = service.Generate();
        var (_, hash2) = service.Generate();

        Assert.False(service.Verify(otp1, hash2));
    }

    [Theory(DisplayName = "Verify: empty OTP returns false")]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_EmptyOtp_ReturnsFalse(string otp)
    {
        var service = CreateService();
        Assert.False(service.Verify(otp, "someHash"));
    }

    [Theory(DisplayName = "Verify: empty hash returns false")]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_EmptyHash_ReturnsFalse(string hash)
    {
        var service = CreateService();
        Assert.False(service.Verify("123456", hash));
    }

    [Fact(DisplayName = "Verify: one digit changed returns false")]
    public void Verify_OtpOffByOne_ReturnsFalse()
    {
        var service = CreateService();
        var (otp, hash) = service.Generate();

        var chars = otp.ToCharArray();
        chars[^1] = chars[^1] == '9' ? '0' : (char)(chars[^1] + 1);
        var modifiedOtp = new string(chars);

        Assert.False(service.Verify(modifiedOtp, hash));
    }

    [Fact(DisplayName = "Verify: same OTP can be verified twice by OtpService")]
    public void Verify_SameOtpTwice_BothReturnTrue()
    {
        var service = CreateService();
        var (otp, hash) = service.Generate();

        Assert.True(service.Verify(otp, hash));
        Assert.True(service.Verify(otp, hash));
    }

    [Fact(DisplayName = "Verify: hash generated with different key returns false")]
    public void Verify_HashFromDifferentKey_ReturnsFalse()
    {
        var service1 = CreateService("first-secret-key-minimum-32-chars!!");
        var service2 = CreateService("second-secret-key-minimum-32-chars!");

        var (otp, hash) = service1.Generate();

        Assert.False(service2.Verify(otp, hash));
    }

    [Fact(DisplayName = "Constructor: missing secret key throws InvalidOperationException")]
    public void Constructor_MissingSecretKey_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Throws<InvalidOperationException>(() => new OtpService(config));
    }

    [Fact(DisplayName = "Constructor: short secret key throws InvalidOperationException")]
    public void Constructor_ShortSecretKey_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OtpSettings:SecretKey"] = "short"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new OtpService(config));
        Assert.Contains("32", ex.Message);
    }

    [Fact(DisplayName = "Constructor: exactly 32 characters succeeds")]
    public void Constructor_ExactlyThirtyTwoChars_Succeeds()
    {
        var key = new string('x', 32);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OtpSettings:SecretKey"] = key
            })
            .Build();

        var service = new OtpService(config);
        Assert.NotNull(service);
    }
}
