using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Auth.Services;

public static class StagingTestSupportPolicy
{
    public static bool IsEnabled(
        IHostEnvironment environment,
        IConfiguration configuration) =>
        environment.IsStaging() &&
        configuration.GetValue<bool>("Staging:TestSupport:Enabled");
}

public sealed class StagingFixedOtpService : IOtpService
{
    private readonly string _fixedOtp;
    private readonly byte[] _secretKey;

    public StagingFixedOtpService(IConfiguration configuration)
    {
        _fixedOtp = configuration["Staging:TestSupport:FixedOtp"]
            ?? throw new InvalidOperationException(
                "Staging:TestSupport:FixedOtp is required when Staging test support is enabled.");

        if (_fixedOtp.Length != 6 || !_fixedOtp.All(char.IsAsciiDigit))
            throw new InvalidOperationException(
                "Staging:TestSupport:FixedOtp must contain exactly six digits.");

        var secret = configuration["OtpSettings:SecretKey"]
            ?? throw new InvalidOperationException("OtpSettings:SecretKey is required.");

        if (secret.Length < 32)
            throw new InvalidOperationException(
                "OtpSettings:SecretKey must be at least 32 characters.");

        _secretKey = Encoding.UTF8.GetBytes(secret);
    }

    public (string otp, string hash) Generate() =>
        (_fixedOtp, ComputeHash(_fixedOtp));

    public bool Verify(string otp, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(otp) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(ComputeHash(otp)),
            Encoding.UTF8.GetBytes(storedHash));
    }

    private string ComputeHash(string otp)
    {
        using var hmac = new HMACSHA256(_secretKey);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(otp)));
    }
}

public sealed class StagingSmokeSmsSink : ISmsService
{
    private readonly string _allowedPhonePrefix;

    public StagingSmokeSmsSink(IConfiguration configuration)
    {
        _allowedPhonePrefix = configuration["Staging:TestSupport:PhonePrefix"]
            ?? throw new InvalidOperationException(
                "Staging:TestSupport:PhonePrefix is required when Staging test support is enabled.");

        if (!_allowedPhonePrefix.StartsWith('+') || _allowedPhonePrefix.Length < 6)
            throw new InvalidOperationException(
                "Staging:TestSupport:PhonePrefix must be an E.164-style test prefix.");
    }

    public Task<bool> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Deliberately do not log the phone number or OTP.
        return Task.FromResult(
            phoneNumber.StartsWith(_allowedPhonePrefix, StringComparison.Ordinal));
    }
}
