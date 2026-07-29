using PropertyApi.Application.Auth.Commands.SocialLogin;

namespace PropertyApi.Application.Auth.Abstractions;

public interface ISocialAuthenticationOrchestrator
{
    Task<SocialLoginResult> AuthenticateAsync(
        SocialLoginCommand command,
        CancellationToken cancellationToken);
}
