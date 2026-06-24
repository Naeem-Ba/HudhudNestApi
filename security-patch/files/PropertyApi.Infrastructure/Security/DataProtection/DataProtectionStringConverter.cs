using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PropertyApi.Infrastructure.Security.DataProtection;

public sealed class DataProtectionStringConverter : ValueConverter<string?, string?>
{
    public DataProtectionStringConverter(
        IDataProtectionProvider dataProtectionProvider,
        string purpose)
        : base(
            value => Protect(dataProtectionProvider, purpose, value),
            value => Unprotect(dataProtectionProvider, purpose, value))
    {
        if (dataProtectionProvider is null)
            throw new ArgumentNullException(nameof(dataProtectionProvider));

        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("A data-protection purpose is required.", nameof(purpose));
    }

    private static string? Protect(
        IDataProtectionProvider provider,
        string purpose,
        string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return provider.CreateProtector(purpose).Protect(value);
    }

    private static string? Unprotect(
        IDataProtectionProvider provider,
        string purpose,
        string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        try
        {
            return provider.CreateProtector(purpose).Unprotect(value);
        }
        catch (CryptographicException)
        {
            // Backward compatibility: existing rows may still contain plaintext values.
            // They will be encrypted the next time the entity is updated and saved.
            return value;
        }
        catch (FormatException)
        {
            return value;
        }
    }
}
