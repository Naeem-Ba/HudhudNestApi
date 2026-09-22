using HudhudNestApi.Application.Auth.Commands.SocialLogin;

namespace HudhudNestApi.Application.Auth.Abstractions;

public interface ISocialAuthenticationOrchestrator
{
    Task<SocialLoginResult> AuthenticateAsync(
        SocialLoginCommand command,
        CancellationToken cancellationToken);
}
