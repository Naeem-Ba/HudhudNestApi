using HudhudNestApi.Application.Auth.Interfaces;

namespace HudhudNestApi.Integration.Tests.TestInfrastructure;

/// <summary>
/// Deterministic OTP service for integration tests.
///
/// This avoids reading OTP values from console logs and makes the HTTP flow
/// fully automated and repeatable.
/// </summary>
public sealed class DeterministicOtpService : IOtpService
{
    public const string ValidOtp = "123456";
    public const string ValidHash = "TEST_HASH_FOR_123456";

    public (string otp, string hash) Generate()
        => (ValidOtp, ValidHash);

    public bool Verify(string otp, string storedHash)
        => otp == ValidOtp && storedHash == ValidHash;
}
