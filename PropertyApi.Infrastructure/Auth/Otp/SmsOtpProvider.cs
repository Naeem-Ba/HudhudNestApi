using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Phone;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Infrastructure.Auth.Otp;

/// <summary>The existing SMS path, unchanged: whichever <see cref="ISmsService"/> the environment registered.</summary>
public sealed class SmsOtpProvider : IOtpProvider
{
    private readonly ISmsService _sms;

    public SmsOtpProvider(ISmsService sms) => _sms = sms;

    public OtpChannel Channel => OtpChannel.Sms;

    public async Task<OtpSendResult> SendAsync(string phoneNumber, string code, CancellationToken ct) =>
        await _sms.SendOtpAsync(phoneNumber, code, ct) ? OtpSendResult.Sent() : OtpSendResult.Unavailable();
}
