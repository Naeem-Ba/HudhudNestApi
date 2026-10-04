using MediatR;
using HudhudNestApi.Application.Agencies.Commands.CreateAgencyInvitation;
using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Exceptions;

namespace HudhudNestApi.Application.Agencies.Commands.CreateAgencyInvitationByContact;

public sealed class CreateAgencyInvitationByContactCommandHandler
    : IRequestHandler<CreateAgencyInvitationByContactCommand, AgencyInvitationDto>
{
    private const string NotFoundMessage = "لا يوجد حساب بهذا البريد أو الرقم.";

    private readonly ILoginIdentityService _emailLookup;
    private readonly IPhoneOtpIdentityService _phoneLookup;
    private readonly IPhoneNumberNormalizer _phoneNormalizer;
    private readonly ISender _sender;

    public CreateAgencyInvitationByContactCommandHandler(
        ILoginIdentityService emailLookup,
        IPhoneOtpIdentityService phoneLookup,
        IPhoneNumberNormalizer phoneNormalizer,
        ISender sender)
    {
        _emailLookup = emailLookup;
        _phoneLookup = phoneLookup;
        _phoneNormalizer = phoneNormalizer;
        _sender = sender;
    }

    public async Task<AgencyInvitationDto> Handle(
        CreateAgencyInvitationByContactCommand request,
        CancellationToken cancellationToken)
    {
        var account = await FindAccountAsync(request, cancellationToken);

        if (account is null || account.IsDeleted)
            throw new NotFoundException(NotFoundMessage);

        // Ownership, active state, member cap, self-invite, existing membership and the
        // duplicate-pending check all live in CreateAgencyInvitationCommandHandler.
        return await _sender.Send(
            new CreateAgencyInvitationCommand(
                AgencyId: request.AgencyId,
                TargetUserId: account.UserAccountId,
                RequestingUserId: request.RequestingUserId,
                IpAddress: request.IpAddress),
            cancellationToken);
    }

    private async Task<IdentityAccountSnapshot?> FindAccountAsync(
        CreateAgencyInvitationByContactCommand request,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            // Same normalisation LoginCommandHandler applies before the same lookup.
            return await _emailLookup.FindByEmailAsync(request.Email.Trim().ToLowerInvariant(), ct);
        }

        var phone = _phoneNormalizer.Normalize(request.PhoneNumber);
        if (!phone.Succeeded || string.IsNullOrWhiteSpace(phone.Value))
            throw new ValidationException("PhoneNumber", "رقم الهاتف غير صالح.");

        return await _phoneLookup.FindByPhoneNumberAsync(phone.Value, ct);
    }
}
