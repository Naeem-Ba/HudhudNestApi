using MediatR;
using PropertyApi.Application.Agencies.DTOs;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Agencies.Mapping;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;

namespace PropertyApi.Application.Agencies.Commands.UpdateAgency;

public sealed class UpdateAgencyCommandHandler
    : IRequestHandler<UpdateAgencyCommand, AgencyDto>
{
    private readonly IAgencyRepository _agencies;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateAgencyCommandHandler(
        IAgencyRepository agencies,
        IUnitOfWork unitOfWork)
    {
        _agencies = agencies;
        _unitOfWork = unitOfWork;
    }

    public async Task<AgencyDto> Handle(
        UpdateAgencyCommand request,
        CancellationToken cancellationToken)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, cancellationToken);

        if (agency is null || agency.IsDeleted)
            throw new NotFoundException("Agency was not found.");

        // Holding AgencyOwner is not enough — it must be THIS agency's owner. Same check as
        // AddAgencyMemberCommandHandler/RemoveAgencyMemberCommandHandler.
        if (agency.OwnerUserId != request.RequestingUserId)
            throw new ForbiddenException("Only the agency owner can update its profile.");

        agency.UpdateProfile(
            name: request.Name,
            description: request.Description,
            contactEmail: request.ContactEmail,
            contactPhone: request.ContactPhone,
            city: request.City,
            utcNow: DateTime.UtcNow);

        // GetByIdAsync returns a tracked entity (see AgencyRepository), so this alone
        // persists the mutation above — no separate Update() call needed.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var members = await _agencies.GetMembersAsync(agency.Id, cancellationToken);

        return AgencyMapper.ToDto(agency, members, members.Count);
    }
}
