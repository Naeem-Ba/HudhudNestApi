using PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;
using PropertyApi.Application.Auth.DTOs;

namespace PropertyApi.Application.Auth.Abstractions;

public interface IPhoneOtpAuthenticationOrchestrator
{
    Task<VerifyOtpResult> AuthenticateAsync(
        VerifyPhoneOtpCommand command,
        CancellationToken cancellationToken);
}
