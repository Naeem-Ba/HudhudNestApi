using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Agencies.Commands.DeactivateAgency;

public sealed class DeactivateAgencyCommandHandler
    : IRequestHandler<DeactivateAgencyCommand, Unit>
{
    private readonly IAgencyRepository _agencies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeactivateAgencyCommandHandler> _logger;

    public DeactivateAgencyCommandHandler(
        IAgencyRepository agencies,
        IUnitOfWork unitOfWork,
        ILogger<DeactivateAgencyCommandHandler> logger)
    {
        _agencies = agencies;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Unit> Handle(
        DeactivateAgencyCommand request,
        CancellationToken cancellationToken)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, cancellationToken);

        if (agency is null || agency.IsDeleted)
            throw new NotFoundException("Agency was not found.");

        if (agency.OwnerUserId != request.RequestingUserId)
            throw new ForbiddenException("Only the agency owner can deactivate the agency.");

        if (!agency.IsActive)
        {
            // Already off. DELETE is idempotent — calling it twice is not an error the
            // caller needs to see, it is the same end state reached again.
            return Unit.Value;
        }

        agency.Deactivate(DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Agency deactivated. AgencyId={AgencyId}, By={RequestingUserId}",
            agency.Id,
            request.RequestingUserId);

        return Unit.Value;
    }
}
