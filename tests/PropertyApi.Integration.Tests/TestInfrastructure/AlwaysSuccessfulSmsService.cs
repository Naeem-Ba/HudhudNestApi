using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Integration.Tests.TestInfrastructure;

/// <summary>
/// SMS test double. It confirms sending without calling any external provider.
/// </summary>
public sealed class AlwaysSuccessfulSmsService : ISmsService
{
    public Task<bool> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }
}
