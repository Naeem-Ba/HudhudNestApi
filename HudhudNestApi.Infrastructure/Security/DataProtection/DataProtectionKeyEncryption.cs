using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HudhudNestApi.Infrastructure.Security.DataProtection;

/// <summary>
/// Optional encryption of the Data Protection key ring (DB security audit S02).
/// Without a certificate the keys are stored as before, readable by anyone holding a database copy.
/// With one, new keys are written encrypted; keys written earlier stay readable.
/// </summary>
public static class DataProtectionKeyEncryption
{
    public const string CertificateKey = "DataProtection:KeyEncryptionCertificate";
    public const string PasswordKey = "DataProtection:KeyEncryptionCertificatePassword";

    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[CertificateKey]);

    /// <summary>Logs a warning when the key ring is stored unencrypted. Returns true when it warned.</summary>
    public static bool WarnWhenUnencrypted(IConfiguration configuration, ILogger logger)
    {
        if (IsConfigured(configuration))
        {
            return false;
        }

        logger.LogWarning(
            "Data Protection keys are stored unencrypted. Set {Setting} to encrypt new keys.",
            CertificateKey);
        return true;
    }

    /// <summary>Loads the base64 PFX, or returns null when none is configured.</summary>
    public static X509Certificate2? LoadCertificate(IConfiguration configuration)
    {
        var encoded = configuration[CertificateKey];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{CertificateKey} must be a base64-encoded PFX file.", ex);
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12(
                bytes,
                configuration[PasswordKey],
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"{CertificateKey} could not be loaded. Check the file and {PasswordKey}.", ex);
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"{CertificateKey} must contain the private key.");
        }

        return certificate;
    }

    /// <summary>Encrypts new keys with the configured certificate; does nothing without one.</summary>
    public static IDataProtectionBuilder ProtectKeysWhenConfigured(
        this IDataProtectionBuilder builder,
        IConfiguration configuration)
    {
        var certificate = LoadCertificate(configuration);
        if (certificate is null)
        {
            return builder;
        }

        return builder
            .ProtectKeysWithCertificate(certificate)
            .UnprotectKeysWithAnyCertificate(certificate);
    }
}
