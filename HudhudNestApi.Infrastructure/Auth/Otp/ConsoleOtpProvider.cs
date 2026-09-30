using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Phone;
using HudhudNestApi.Application.Common.Security;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Infrastructure.Auth.Otp;

/// <summary>
/// Development / Testing / CI stand-in for Telegram and WhatsApp, like ConsoleSmsService for SMS: the code is
/// printed so the flow can be completed without a real provider. Never registered in Staging or Production.
/// </summary>
public sealed class ConsoleOtpProvider : IOtpProvider
{
    private readonly ILogger<ConsoleOtpProvider> _logger;

    public ConsoleOtpProvider(OtpChannel channel, ILogger<ConsoleOtpProvider> logger)
    {
        Channel = channel;
        _logger = logger;
    }

    public OtpChannel Channel { get; }

    public Task<OtpSendResult> SendAsync(string phoneNumber, string code, CancellationToken ct)
    {
        _logger.LogWarning("  [DEV MODE] OTP via {Channel} for {Phone}: {OTP}", Channel, PiiMasking.MaskPhone(phoneNumber), code);
        return Task.FromResult(OtpSendResult.Sent());
    }
}
