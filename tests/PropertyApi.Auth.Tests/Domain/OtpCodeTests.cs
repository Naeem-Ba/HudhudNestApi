namespace PropertyApi.Auth.Tests.Domain;

[Trait("Category", "Domain")]
[Trait("Entity", "OtpCode")]
public sealed class OtpCodeTests
{
    [Fact(DisplayName = "Create: valid inputs set all properties correctly")]
    public void Create_ValidInputs_SetsAllPropertiesCorrectly()
    {
        var phone = "+963911234567";
        var hash = "someHashValue==";
        var purpose = OtpPurpose.PhoneRegistration;
        var ip = "192.168.1.1";

        var beforeCreate = DateTime.UtcNow;
        var code = OtpCode.Create(phone, hash, purpose, ip, expiryMinutes: 5);
        var afterCreate = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, code.Id);
        Assert.Equal(phone, code.PhoneNumber);
        Assert.Equal(hash, code.CodeHash);
        Assert.Equal(purpose, code.Purpose);
        Assert.Equal(ip, code.IpAddress);
        Assert.Equal(0, code.AttemptCount);
        Assert.False(code.IsUsed);
        Assert.InRange(code.CreatedAt, beforeCreate, afterCreate);
        Assert.InRange(code.ExpiresAt, beforeCreate.AddMinutes(5), afterCreate.AddMinutes(5));
    }

    [Theory(DisplayName = "Create: invalid phone throws ArgumentException")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_InvalidPhoneNumber_ThrowsArgumentException(string phoneNumber)
    {
        Assert.Throws<ArgumentException>(() =>
            OtpCode.Create(phoneNumber, "hash", OtpPurpose.PhoneRegistration));
    }

    [Theory(DisplayName = "Create: invalid hash throws ArgumentException")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_InvalidCodeHash_ThrowsArgumentException(string codeHash)
    {
        Assert.Throws<ArgumentException>(() =>
            OtpCode.Create("+963911234567", codeHash, OtpPurpose.PhoneRegistration));
    }

    [Fact(DisplayName = "Create: trims phone number")]
    public void Create_PhoneWithLeadingTrailingSpaces_TrimsPhone()
    {
        var code = OtpCode.Create("  +963911234567  ", "hash", OtpPurpose.PhoneRegistration);
        Assert.Equal("+963911234567", code.PhoneNumber);
    }

    [Fact(DisplayName = "Create: two calls generate different IDs")]
    public void Create_TwoCalls_GeneratesDifferentIds()
    {
        var code1 = OtpCode.Create("+963911111111", "hash1", OtpPurpose.PhoneRegistration);
        var code2 = OtpCode.Create("+963922222222", "hash2", OtpPurpose.PhoneRegistration);

        Assert.NotEqual(code1.Id, code2.Id);
    }

    [Fact(DisplayName = "Create: default expiry is about five minutes")]
    public void Create_DefaultExpiry_SetsExpiryToFiveMinutes()
    {
        var before = DateTime.UtcNow;
        var code = OtpCode.Create("+963911234567", "hash", OtpPurpose.PhoneRegistration);
        var after = DateTime.UtcNow;

        Assert.InRange(code.ExpiresAt,
            before.AddMinutes(4).AddSeconds(59),
            after.AddMinutes(5).AddSeconds(1));
    }

    [Theory(DisplayName = "Create: custom expiry is applied")]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    public void Create_CustomExpiry_SetsCorrectExpiry(int minutes)
    {
        var before = DateTime.UtcNow;
        var code = OtpCode.Create("+963911234567", "hash", OtpPurpose.PhoneRegistration, expiryMinutes: minutes);
        var after = DateTime.UtcNow;

        Assert.InRange(code.ExpiresAt,
            before.AddMinutes(minutes).AddSeconds(-1),
            after.AddMinutes(minutes).AddSeconds(1));
    }

    [Fact(DisplayName = "Create: without IP stores null")]
    public void Create_NoIpAddress_SetsIpAddressToNull()
    {
        var code = OtpCode.Create("+963911234567", "hash", OtpPurpose.PhoneRegistration);
        Assert.Null(code.IpAddress);
    }

    [Fact(DisplayName = "IsExpired: fresh code returns false")]
    public void IsExpired_FreshCode_ReturnsFalse()
    {
        var code = OtpCode.Create("+963911234567", "hash", OtpPurpose.PhoneRegistration, expiryMinutes: 5);
        Assert.False(code.IsExpired());
    }

    [Fact(DisplayName = "IsExpired: expired code returns true")]
    public void IsExpired_ExpiredCode_ReturnsTrue()
    {
        var code = OtpCode.Create("+963911234567", "hash", OtpPurpose.PhoneRegistration, expiryMinutes: -1);
        Assert.True(code.IsExpired());
    }

    [Fact(DisplayName = "SecondsRemaining: expired code returns zero")]
    public void SecondsRemaining_ExpiredCode_ReturnsZero()
    {
        var code = OtpCode.Create("+963911234567", "hash", OtpPurpose.PhoneRegistration, expiryMinutes: -1);
        Assert.Equal(0, code.SecondsRemaining());
    }

    [Fact(DisplayName = "SecondsRemaining: valid code returns positive value")]
    public void SecondsRemaining_ValidCode_ReturnsPositiveValue()
    {
        var code = OtpCode.Create("+963911234567", "hash", OtpPurpose.PhoneRegistration, expiryMinutes: 5);

        var remaining = code.SecondsRemaining();

        Assert.True(remaining > 0);
        Assert.True(remaining <= 300);
    }

    [Theory(DisplayName = "IsExhausted: less than three attempts returns false")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IsExhausted_LessThanThreeAttempts_ReturnsFalse(int attempts)
    {
        var code = OtpCode.Create("+963911234567", "hash", OtpPurpose.PhoneRegistration);

        for (var i = 0; i < attempts; i++)
            code.IncrementAttempts();

        Assert.False(code.IsExhausted());
    }

    [Theory(DisplayName = "IsExhausted: three or more attempts returns true")]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(10)]
    public void IsExhausted_ThreeOrMoreAttempts_ReturnsTrue(int attempts)
    {
        var code = OtpCode.Create("+963911234567", "hash", OtpPurpose.PhoneRegistration);

        for (var i = 0; i < attempts; i++)
            code.IncrementAttempts();

        Assert.True(code.IsExhausted());
    }

    [Fact(DisplayName = "IsValid: fresh code returns true")]
    public void IsValid_FreshCode_ReturnsTrue()
    {
        Assert.True(OtpCodeBuilder.Valid().IsValid());
    }

    [Fact(DisplayName = "IsValid: expired code returns false")]
    public void IsValid_ExpiredCode_ReturnsFalse()
    {
        Assert.False(OtpCodeBuilder.Expired().IsValid());
    }

    [Fact(DisplayName = "IsValid: used code returns false")]
    public void IsValid_UsedCode_ReturnsFalse()
    {
        Assert.False(OtpCodeBuilder.Used().IsValid());
    }

    [Fact(DisplayName = "IsValid: exhausted code returns false")]
    public void IsValid_ExhaustedCode_ReturnsFalse()
    {
        Assert.False(OtpCodeBuilder.Exhausted().IsValid());
    }

    [Fact(DisplayName = "IncrementAttempts: increments attempt count")]
    public void IncrementAttempts_IncrementsAttemptCount()
    {
        var code = OtpCodeBuilder.Valid();

        code.IncrementAttempts();
        code.IncrementAttempts();
        code.IncrementAttempts();

        Assert.Equal(3, code.AttemptCount);
    }

    [Fact(DisplayName = "MarkAsUsed: sets IsUsed to true")]
    public void MarkAsUsed_SetsIsUsedToTrue()
    {
        var code = OtpCodeBuilder.Valid();

        code.MarkAsUsed();

        Assert.True(code.IsUsed);
    }

    [Fact(DisplayName = "MarkAsUsed: can be called twice")]
    public void MarkAsUsed_CalledTwice_StillTrue()
    {
        var code = OtpCodeBuilder.Valid();

        code.MarkAsUsed();
        code.MarkAsUsed();

        Assert.True(code.IsUsed);
    }

    [Fact(DisplayName = "Create: all declared OtpPurpose values are accepted")]
    public void Create_AllDeclaredOtpPurposes_CreateSuccessfully()
    {
        foreach (var purpose in Enum.GetValues<OtpPurpose>())
        {
            var code = OtpCode.Create("+963911234567", "hash", purpose);
            Assert.Equal(purpose, code.Purpose);
        }
    }
}
