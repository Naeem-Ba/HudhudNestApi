using MediatR;
using HudhudNestApi.Application.Auth.Abstractions;

namespace HudhudNestApi.Application.Auth.Commands.SocialLogin;

public sealed class SocialLoginCommandHandler
    : IRequestHandler<SocialLoginCommand, SocialLoginResult>
{
    private readonly ISocialAuthenticationOrchestrator _orchestrator;

    public SocialLoginCommandHandler(ISocialAuthenticationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<SocialLoginResult> Handle(
        SocialLoginCommand request,
        CancellationToken cancellationToken) =>
        _orchestrator.AuthenticateAsync(request, cancellationToken);
}
