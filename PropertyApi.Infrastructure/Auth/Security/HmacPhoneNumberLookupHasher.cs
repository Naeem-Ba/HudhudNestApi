using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Auth.Security;

public sealed class HmacPhoneNumberLookupHasher
    : IPhoneNumberLookupHasher
{
    private readonly byte[] _key;

    public HmacPhoneNumberLookupHasher(
        IConfiguration configuration)
    {
        var configuredKey =
            configuration[
                "Security:PhoneLookupHmacKey"];

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException(
                "Security:PhoneLookupHmacKey is not configured.");
        }

        try
        {
            _key = Convert.FromBase64String(
                configuredKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Security:PhoneLookupHmacKey must be a valid Base64 value.",
                ex);
        }

        if (_key.Length < 32)
        {
            throw new InvalidOperationException(
                "Security:PhoneLookupHmacKey must contain at least 32 bytes.");
        }
    }

    public string Compute(
        string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            phoneNumber);

        var normalized =
            Normalize(phoneNumber);

        var data =
            Encoding.UTF8.GetBytes(
                normalized);

        var hash =
            HMACSHA256.HashData(
                _key,
                data);

        return Convert.ToHexString(hash);
    }

    private static string Normalize(
        string phoneNumber)
        => phoneNumber.Trim();
}