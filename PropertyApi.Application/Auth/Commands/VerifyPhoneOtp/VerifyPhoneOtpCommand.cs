using MediatR;
using PropertyApi.Application.Auth.Abstractions;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;

public sealed record VerifyPhoneOtpCommand(
    string PhoneNumber,
    string Code,
    OtpPurpose Purpose = OtpPurpose.PhoneRegistration,
    string? FirstName = null,
    string? LastName = null,
    string? IpAddress = null) : IRequest<VerifyOtpResult>;

public sealed class VerifyPhoneOtpCommandHandler
    : IRequestHandler<VerifyPhoneOtpCommand, VerifyOtpResult>
{
    private readonly IPhoneOtpAuthenticationOrchestrator _orchestrator;

    public VerifyPhoneOtpCommandHandler(IPhoneOtpAuthenticationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<VerifyOtpResult> Handle(
        VerifyPhoneOtpCommand request,
        CancellationToken cancellationToken) =>
        _orchestrator.AuthenticateAsync(request, cancellationToken);
}
