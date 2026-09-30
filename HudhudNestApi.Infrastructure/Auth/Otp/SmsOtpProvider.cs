using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Phone;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Infrastructure.Auth.Otp;

/// <summary>The existing SMS path, unchanged: whichever <see cref="ISmsService"/> the environment registered.</summary>
public sealed class SmsOtpProvider : IOtpProvider
{
    private readonly ISmsService _sms;

    public SmsOtpProvider(ISmsService sms) => _sms = sms;

    public OtpChannel Channel => OtpChannel.Sms;

    public async Task<OtpSendResult> SendAsync(string phoneNumber, string code, CancellationToken ct) =>
        await _sms.SendOtpAsync(phoneNumber, code, ct) ? OtpSendResult.Sent() : OtpSendResult.Unavailable();
}
