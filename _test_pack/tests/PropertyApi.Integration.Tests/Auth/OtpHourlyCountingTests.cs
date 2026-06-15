using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Auth.Commands.SendPhoneOtp;
using PropertyApi.Domain.Enums;
using PropertyApi.Integration.Tests.TestInfrastructure;

namespace PropertyApi.Integration.Tests.Auth;

[Trait("Category", "Integration")]
[Trait("Feature", "OTP")]
public sealed class OtpHourlyCountingTests : IAsyncLifetime
{
    private TestApplication _app = null!;

    public Task InitializeAsync()
    {
        _app = new TestApplication();
        _ = _app.Services;
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "SendPhoneOtp allows three OTP sends within one hour, then returns RATE_LIMITED")]
    public async Task CanSendOtp_ShouldCount_WithinLastHour()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var phoneNumber = CreateUniqueGermanPhoneNumber();

        for (var i = 1; i <= 3; i++)
        {
            var allowedResult = await sender.Send(new SendPhoneOtpCommand(
                PhoneNumber: phoneNumber,
                Purpose: OtpPurpose.PhoneRegistration,
                IpAddress: "127.0.0.1"));

            Assert.True(
                allowedResult.Success,
                $"Expected OTP request #{i} to succeed. Error: {allowedResult.ErrorCode} - {allowedResult.ErrorMessage}");
        }

        var blockedResult = await sender.Send(new SendPhoneOtpCommand(
            PhoneNumber: phoneNumber,
            Purpose: OtpPurpose.PhoneRegistration,
            IpAddress: "127.0.0.1"));

        Assert.False(blockedResult.Success);
        Assert.Equal("RATE_LIMITED", blockedResult.ErrorCode);
        Assert.Equal(3600, blockedResult.RetryAfterSeconds);
    }

    [Fact(DisplayName = "SendPhoneOtp hourly counter resets after the one-hour window")]
    public async Task CanSendOtp_ShouldReset_AfterOneHour()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var repository = scope.ServiceProvider.GetRequiredService<InMemoryOtpCodeRepository>();
        var phoneNumber = CreateUniqueGermanPhoneNumber();

        for (var i = 0; i < 3; i++)
        {
            var result = await sender.Send(new SendPhoneOtpCommand(
                PhoneNumber: phoneNumber,
                Purpose: OtpPurpose.PhoneRegistration,
                IpAddress: "127.0.0.1"));

            Assert.True(result.Success);
        }

        var blockedResult = await sender.Send(new SendPhoneOtpCommand(
            PhoneNumber: phoneNumber,
            Purpose: OtpPurpose.PhoneRegistration,
            IpAddress: "127.0.0.1"));

        Assert.False(blockedResult.Success);
        Assert.Equal("RATE_LIMITED", blockedResult.ErrorCode);

        repository.AdvanceTimeBy(TimeSpan.FromMinutes(61));

        var afterWindowResult = await sender.Send(new SendPhoneOtpCommand(
            PhoneNumber: phoneNumber,
            Purpose: OtpPurpose.PhoneRegistration,
            IpAddress: "127.0.0.1"));

        Assert.True(
            afterWindowResult.Success,
            $"Expected OTP request after one hour to succeed. Error: {afterWindowResult.ErrorCode} - {afterWindowResult.ErrorMessage}");
    }

    private static string CreateUniqueGermanPhoneNumber()
    {
        var suffix = Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L);
        return $"+49{suffix:D13}";
    }
}
