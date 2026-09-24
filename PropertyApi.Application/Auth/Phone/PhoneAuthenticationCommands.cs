using MediatR;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Auth.Phone;

public sealed record SendPhoneChallengeCommand(string PhoneNumber, OtpPurpose Purpose,
    Guid? UserId, string? IpAddress, OtpChannel Channel = OtpChannel.Sms) : IRequest<PhoneWorkflowResult>;
public sealed record RegisterPhoneCommand(Guid ChallengeId, string Code, string Password,
    string FirstName, string LastName, string? IpAddress, OtpChannel? Channel = null) : IRequest<PhoneWorkflowResult>;
public sealed record LoginPhoneCommand(string PhoneNumber, string Password, string? IpAddress)
    : IRequest<PhoneWorkflowResult>;
public sealed record VerifyPhonePasswordResetCommand(Guid ChallengeId, string Code, OtpChannel? Channel = null)
    : IRequest<PhoneWorkflowResult>;
public sealed record ConfirmPhonePasswordResetCommand(string PhoneNumber, string ConfirmationToken,
    string NewPassword, string? IpAddress) : IRequest<PhoneWorkflowResult>;
public sealed record GetPhoneReverificationStatusQuery(Guid UserId) : IRequest<PhoneWorkflowResult>;
public sealed record VerifyPhoneReverificationCommand(Guid UserId, Guid ChallengeId, string Code, OtpChannel? Channel = null)
    : IRequest<PhoneWorkflowResult>;
public sealed record VerifyPhoneChangeCommand(Guid UserId, Guid ChallengeId, string Code,
    string CurrentPassword, string? IpAddress, OtpChannel? Channel = null) : IRequest<PhoneWorkflowResult>;

public sealed class PhoneAuthenticationCommandHandler :
    IRequestHandler<SendPhoneChallengeCommand, PhoneWorkflowResult>,
    IRequestHandler<RegisterPhoneCommand, PhoneWorkflowResult>,
    IRequestHandler<LoginPhoneCommand, PhoneWorkflowResult>,
    IRequestHandler<VerifyPhonePasswordResetCommand, PhoneWorkflowResult>,
    IRequestHandler<ConfirmPhonePasswordResetCommand, PhoneWorkflowResult>,
    IRequestHandler<GetPhoneReverificationStatusQuery, PhoneWorkflowResult>,
    IRequestHandler<VerifyPhoneReverificationCommand, PhoneWorkflowResult>,
    IRequestHandler<VerifyPhoneChangeCommand, PhoneWorkflowResult>
{
    private readonly IPhoneAuthenticationWorkflow _workflow;
    public PhoneAuthenticationCommandHandler(IPhoneAuthenticationWorkflow workflow) => _workflow = workflow;
    public Task<PhoneWorkflowResult> Handle(SendPhoneChallengeCommand r, CancellationToken ct) => _workflow.SendOtpAsync(r.PhoneNumber, r.Purpose, r.UserId, r.IpAddress, ct, r.Channel);
    public Task<PhoneWorkflowResult> Handle(RegisterPhoneCommand r, CancellationToken ct) => _workflow.RegisterAsync(r.ChallengeId, r.Code, r.Password, r.FirstName, r.LastName, r.IpAddress, ct, r.Channel);
    public Task<PhoneWorkflowResult> Handle(LoginPhoneCommand r, CancellationToken ct) => _workflow.LoginAsync(r.PhoneNumber, r.Password, r.IpAddress, ct);
    public Task<PhoneWorkflowResult> Handle(VerifyPhonePasswordResetCommand r, CancellationToken ct) => _workflow.VerifyPasswordResetAsync(r.ChallengeId, r.Code, ct, r.Channel);
    public Task<PhoneWorkflowResult> Handle(ConfirmPhonePasswordResetCommand r, CancellationToken ct) => _workflow.ConfirmPasswordResetAsync(r.PhoneNumber, r.ConfirmationToken, r.NewPassword, r.IpAddress, ct);
    public Task<PhoneWorkflowResult> Handle(GetPhoneReverificationStatusQuery r, CancellationToken ct) => _workflow.GetReverificationStatusAsync(r.UserId, ct);
    public Task<PhoneWorkflowResult> Handle(VerifyPhoneReverificationCommand r, CancellationToken ct) => _workflow.VerifyReverificationAsync(r.UserId, r.ChallengeId, r.Code, ct, r.Channel);
    public Task<PhoneWorkflowResult> Handle(VerifyPhoneChangeCommand r, CancellationToken ct) => _workflow.VerifyPhoneChangeAsync(r.UserId, r.ChallengeId, r.Code, r.CurrentPassword, r.IpAddress, ct, r.Channel);
}

/// <summary>
/// Which delivery channels the picker should offer for a number. A missing or malformed number is not an error:
/// it is answered with the country-independent picture, so the endpoint never becomes a phone-number validator.
/// </summary>
public sealed record GetOtpChannelsQuery(string? PhoneNumber) : IRequest<IReadOnlyList<OtpChannelAvailability>>;

public sealed class GetOtpChannelsQueryHandler : IRequestHandler<GetOtpChannelsQuery, IReadOnlyList<OtpChannelAvailability>>
{
    private readonly IOtpChannelService _channels;
    private readonly IPhoneNumberNormalizer _normalizer;

    public GetOtpChannelsQueryHandler(IOtpChannelService channels, IPhoneNumberNormalizer normalizer)
    {
        _channels = channels;
        _normalizer = normalizer;
    }

    public Task<IReadOnlyList<OtpChannelAvailability>> Handle(GetOtpChannelsQuery request, CancellationToken ct)
    {
        var normalized = _normalizer.Normalize(request.PhoneNumber);
        return Task.FromResult(_channels.Describe(normalized.Succeeded ? normalized.Value : null));
    }
}
