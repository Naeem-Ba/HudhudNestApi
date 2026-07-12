using Xunit;

namespace PropertyApi.Integration.Tests.Auth;

[Collection("AuthPostgres")]
public sealed class PhoneOtpAtomicityPostgresTests
{
    [Fact]
    public async Task UserCreationFailure_LeavesOtpUnused()
    {
        await using var factory = new PostgresAuthTestFactory(
            new AuthFaultPlan { FailCreateAsync = true });
        await factory.PrepareDatabaseAsync();

        var phone = UniquePhone();
        var code = await SendAndCaptureOtpAsync(factory, phone);
        var result = await VerifyAsync(factory, phone, code);

        Assert.False(AuthTestReflection.IsSucceeded(result));
        Assert.Equal(0, await AuthDbAssertions.UserCountByPhoneAsync(factory, phone));
        Assert.False((await AuthDbAssertions.LatestOtpStateAsync(factory, phone)).IsUsed);
        Assert.Equal(0, await AuthDbAssertions.UserAccountTotalCountAsync(factory));
    }

    [Fact]
    public async Task RoleFailure_RollsBackUser_AndLeavesOtpUnused()
    {
        await using var factory = new PostgresAuthTestFactory(
            new AuthFaultPlan { FailAddToRoleAsync = true });
        await factory.PrepareDatabaseAsync();

        var phone = UniquePhone();
        var code = await SendAndCaptureOtpAsync(factory, phone);
        var result = await VerifyAsync(factory, phone, code);

        Assert.False(AuthTestReflection.IsSucceeded(result));
        Assert.Equal(0, await AuthDbAssertions.UserCountByPhoneAsync(factory, phone));
        Assert.False((await AuthDbAssertions.LatestOtpStateAsync(factory, phone)).IsUsed);
        Assert.Equal(0, await AuthDbAssertions.UserAccountTotalCountAsync(factory));
    }

    [Fact]
    public async Task RefreshTokenFailure_RollsBackUser_AndLeavesOtpUnused()
    {
        await using var factory = new PostgresAuthTestFactory(
            new AuthFaultPlan { FailRefreshTokenAddAsync = true });
        await factory.PrepareDatabaseAsync();

        var phone = UniquePhone();
        var code = await SendAndCaptureOtpAsync(factory, phone);

        await AssertFailureOrExceptionAsync(() => VerifyAsync(factory, phone, code));

        Assert.Equal(0, await AuthDbAssertions.UserCountByPhoneAsync(factory, phone));
        Assert.False((await AuthDbAssertions.LatestOtpStateAsync(factory, phone)).IsUsed);
        Assert.Equal(0, await AuthDbAssertions.UserAccountTotalCountAsync(factory));
    }

    [Fact]
    public async Task Success_CreatesUserWithRole_AndConsumesOtp()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        var phone = UniquePhone();
        var code = await SendAndCaptureOtpAsync(factory, phone);
        var result = await VerifyAsync(factory, phone, code);

        Assert.True(AuthTestReflection.IsSucceeded(result));

        var user = await AuthDbAssertions.UserByPhoneAsync(factory, phone);
        Assert.NotNull(user);
        Assert.Equal(1, await AuthDbAssertions.UserAccountCountAsync(factory, user.Value.UserId));
        Assert.Equal(1, await AuthDbAssertions.RoleCountAsync(factory, user!.Value.UserId));
        Assert.True((await AuthDbAssertions.LatestOtpStateAsync(factory, phone)).IsUsed);

    }

    [Fact]
    public async Task ConcurrentVerification_WithSameOtp_OnlyOneSucceeds()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        var phone = UniquePhone();
        var code = await SendAndCaptureOtpAsync(factory, phone);

        var results = await Task.WhenAll(
            VerifyOutcomeAsync(factory, phone, code),
            VerifyOutcomeAsync(factory, phone, code));
        var user =
    await AuthDbAssertions.UserByPhoneAsync(
        factory,
        phone);

        Assert.NotNull(user);
        Assert.Equal(1, await AuthDbAssertions.UserAccountCountAsync(factory, user.Value.UserId));
        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, await AuthDbAssertions.UserCountByPhoneAsync(factory, phone));
        Assert.True((await AuthDbAssertions.LatestOtpStateAsync(factory, phone)).IsUsed);
    }

    [Fact]
    public async Task ExistingUser_ConsumesOtp_PersistsPhoneConfirmation_AndReturnsToken()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        var phone = UniquePhone();
        var email = $"phone-existing-{Guid.NewGuid():N}@example.test";
        await factory.SeedUserAsync(
            email,
            phoneNumber: phone,
            phoneConfirmed: false,
            emailConfirmed: true);

        var code = await SendAndCaptureOtpAsync(factory, phone);
        var result = await VerifyAsync(factory, phone, code);

        Assert.True(AuthTestReflection.IsSucceeded(result));
        var user = await AuthDbAssertions.UserByPhoneAsync(factory, phone);
        Assert.NotNull(user);
        Assert.True(user!.Value.PhoneConfirmed);
        Assert.True((await AuthDbAssertions.LatestOtpStateAsync(factory, phone)).IsUsed);
        Assert.True(HasNonEmptyToken(result));
    }

    [Fact]
    public async Task WrongOtp_IncrementsAttemptCount_AndDoesNotCreateUser()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        var phone = UniquePhone();
        var realCode = await SendAndCaptureOtpAsync(factory, phone);
        var before = await AuthDbAssertions.LatestOtpStateAsync(factory, phone);
        var wrongCode = realCode == "000000" ? "999999" : "000000";

        var result = await VerifyAsync(factory, phone, wrongCode);
        var after = await AuthDbAssertions.LatestOtpStateAsync(factory, phone);

        Assert.False(AuthTestReflection.IsSucceeded(result));
        Assert.Equal(before.AttemptCount + 1, after.AttemptCount);
        Assert.False(after.IsUsed);
        Assert.Equal(0, await AuthDbAssertions.UserCountByPhoneAsync(factory, phone));
    }

    private static async Task<string> SendAndCaptureOtpAsync(
        PostgresAuthTestFactory factory,
        string phoneNumber)
    {
        await factory.InScopeAsync(sp =>
            AuthTestReflection.SendAsync(sp, AuthTestReflection.SendOtpCommand(phoneNumber)));

        return factory.Sms.GetCode(phoneNumber);
    }

    private static Task<object?> VerifyAsync(
        PostgresAuthTestFactory factory,
        string phoneNumber,
        string code)
        => factory.InScopeAsync(sp =>
            AuthTestReflection.SendAsync(
                sp,
                AuthTestReflection.VerifyOtpCommand(phoneNumber, code)));

    private static async Task<bool> VerifyOutcomeAsync(
        PostgresAuthTestFactory factory,
        string phoneNumber,
        string code)
    {
        try
        {
            var result = await VerifyAsync(factory, phoneNumber, code);
            return AuthTestReflection.IsSucceeded(result);
        }
        catch
        {
            return false;
        }
    }

    private static string UniquePhone()
    {
        var suffix = Random.Shared.Next(10_000_000, 99_999_999);
        return $"+4915{suffix}";
    }

    private static bool HasNonEmptyToken(object? result)
    {
        if (result is null)
        {
            return false;
        }

        foreach (var name in new[] { "AccessToken", "Token", "JwtToken" })
        {
            var property = result.GetType().GetProperty(name);
            if (property?.PropertyType == typeof(string) &&
                !string.IsNullOrWhiteSpace((string?)property.GetValue(result)))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task AssertFailureOrExceptionAsync(Func<Task<object?>> action)
    {
        try
        {
            var result = await action();
            Assert.False(AuthTestReflection.IsSucceeded(result));
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("Injected", StringComparison.OrdinalIgnoreCase))
        {
            // Expected injected infrastructure failure.
        }
    }
}
