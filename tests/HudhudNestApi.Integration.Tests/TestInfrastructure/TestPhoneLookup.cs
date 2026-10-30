using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Auth.Interfaces;

namespace HudhudNestApi.Integration.Tests.TestInfrastructure;

/// <summary>
/// Accounts and OTP challenges are found by the phone number's lookup hash: the number itself is not
/// stored in a searchable column any more (DB security audit 2026-10-29, S01).
/// </summary>
internal static class TestPhoneLookup
{
    public static string Hash(IServiceProvider services, string phone) =>
        services.GetRequiredService<IPhoneNumberLookupHasher>().Compute(phone);
}
