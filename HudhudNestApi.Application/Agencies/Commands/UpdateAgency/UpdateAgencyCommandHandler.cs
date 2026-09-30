using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Mapping;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Agencies.Commands.UpdateAgency;

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

        // Full replace, same semantics as the fields above (UpdateAgencyCommand's doc
        // comment): whatever the request carries — including null — becomes the new
        // value. This is what lets an owner clear District/Neighborhood by omitting
        // them, and what lets an old agency with no location add one for the first
        // time. Hierarchy consistency is enforced by UpdateAgencyCommandValidator
        // before the handler runs.
        agency.GovernorateId = request.GovernorateId;
        agency.DistrictId = request.DistrictId;
        agency.NeighborhoodId = request.NeighborhoodId;

        // GetByIdAsync returns a tracked entity (see AgencyRepository), so this alone
        // persists the mutation above — no separate Update() call needed.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var members = await _agencies.GetMembersAsync(agency.Id, cancellationToken);

        return AgencyMapper.ToDto(agency, members, members.Count);
    }
}
