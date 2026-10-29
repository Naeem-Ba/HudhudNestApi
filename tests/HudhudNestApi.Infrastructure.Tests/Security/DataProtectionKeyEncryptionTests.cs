using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Infrastructure.Security.DataProtection;
using Xunit;

namespace HudhudNestApi.Infrastructure.Tests.Security;

/// <summary>DB security audit S02: the key ring can be encrypted with a certificate kept outside the database.</summary>
public sealed class DataProtectionKeyEncryptionTests : IDisposable
{
    private readonly DirectoryInfo _keys = Directory.CreateTempSubdirectory("dp-keys-");

    public void Dispose() => _keys.Delete(recursive: true);

    [Fact(DisplayName = "With a certificate, new keys are stored encrypted and still read back")]
    public void Certificate_EncryptsNewKeys_AndTheyStayReadable()
    {
        var configuration = Config(CreatePfxBase64("secret"), "secret");

        var protectedValue = Protector(configuration).Protect("+963900000001");

        var keyXml = File.ReadAllText(_keys.GetFiles("key-*.xml").Single().FullName);
        Assert.Contains("encryptedSecret", keyXml, StringComparison.Ordinal);
        Assert.DoesNotContain("unencrypted form", keyXml, StringComparison.Ordinal);

        // A fresh process with the same certificate reads the stored key.
        Assert.Equal("+963900000001", Protector(configuration).Unprotect(protectedValue));
    }

    [Fact(DisplayName = "Keys written before the certificate was added stay readable")]
    public void OldUnencryptedKeys_StayReadable_AfterAddingTheCertificate()
    {
        var protectedValue = Protector(Config(null, null)).Protect("tax-123");

        var withCertificate = Config(CreatePfxBase64(null), null);

        Assert.Equal("tax-123", Protector(withCertificate).Unprotect(protectedValue));
    }

    [Fact(DisplayName = "Without a certificate the builder is left as it was")]
    public void NoCertificate_ReturnsNull()
    {
        Assert.Null(DataProtectionKeyEncryption.LoadCertificate(Config(null, null)));
        Assert.False(DataProtectionKeyEncryption.IsConfigured(Config("  ", null)));
    }

    [Fact(DisplayName = "A broken certificate setting fails startup with a clear message")]
    public void BrokenCertificate_Throws()
    {
        var notBase64 = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyEncryption.LoadCertificate(Config("not base64!", null)));
        Assert.Contains("base64", notBase64.Message, StringComparison.Ordinal);

        var wrongPassword = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyEncryption.LoadCertificate(Config(CreatePfxBase64("right"), "wrong")));
        Assert.Contains("could not be loaded", wrongPassword.Message, StringComparison.Ordinal);

        using var rsa = RSA.Create(2048);
        using var publicOnly = X509CertificateLoader.LoadCertificate(SelfSigned(rsa).Export(X509ContentType.Cert));
        var noKey = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyEncryption.LoadCertificate(
                Config(Convert.ToBase64String(publicOnly.Export(X509ContentType.Pkcs12)), null)));
        Assert.Contains("private key", noKey.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Startup warns only while the key ring is unencrypted")]
    public void Warning_OnlyWithoutCertificate()
    {
        Assert.True(DataProtectionKeyEncryption.WarnWhenUnencrypted(Config(null, null), NullLogger.Instance));
        Assert.False(DataProtectionKeyEncryption.WarnWhenUnencrypted(Config("x", null), NullLogger.Instance));
    }

    private IDataProtector Protector(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection()
            .SetApplicationName("HudhudNestApi")
            .ProtectKeysWhenConfigured(configuration)
            .PersistKeysToFileSystem(_keys);
        return services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("tests");
    }

    private static IConfiguration Config(string? certificate, string? password) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataProtectionKeyEncryption.CertificateKey] = certificate,
                [DataProtectionKeyEncryption.PasswordKey] = password,
            })
            .Build();

    private static string CreatePfxBase64(string? password)
    {
        using var rsa = RSA.Create(2048);
        using var certificate = SelfSigned(rsa);
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, password));
    }

    private static X509Certificate2 SelfSigned(RSA rsa)
    {
        var request = new CertificateRequest("CN=hudhudnest-dp-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }
}
