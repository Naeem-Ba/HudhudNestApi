using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Agencies.Commands.ReactivateAgency;

public sealed class ReactivateAgencyCommandHandler : IRequestHandler<ReactivateAgencyCommand, Unit>
{
    private readonly IAgencyRepository _agencies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReactivateAgencyCommandHandler> _logger;

    public ReactivateAgencyCommandHandler(
        IAgencyRepository agencies,
        IUnitOfWork unitOfWork,
        ILogger<ReactivateAgencyCommandHandler> logger)
    {
        _agencies = agencies;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Unit> Handle(ReactivateAgencyCommand request, CancellationToken cancellationToken)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, cancellationToken);

        if (agency is null || agency.IsDeleted)
            throw new NotFoundException("Agency was not found.");

        // Same check as DeactivateAgencyCommandHandler: the role says the caller owns *an*
        // agency, only OwnerUserId says it is *this* one.
        if (agency.OwnerUserId != request.RequestingUserId)
            throw new ForbiddenException("Only the agency owner can reactivate the agency.");

        if (agency.IsActive)
            return Unit.Value;

        agency.Activate(DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Agency reactivated. AgencyId={AgencyId}, By={RequestingUserId}",
            agency.Id,
            request.RequestingUserId);

        return Unit.Value;
    }
}
