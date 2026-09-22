using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;

namespace HudhudNestApi.Application.Listings.Commands.ConfirmPropertyAvailability;

public sealed class ConfirmPropertyAvailabilityCommandHandler
    : IRequestHandler<ConfirmPropertyAvailabilityCommand>
{
    private readonly IPropertyRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmPropertyAvailabilityCommandHandler(
        IPropertyRepository repository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(
        ConfirmPropertyAvailabilityCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _repository.GetByIdAsync(request.PropertyId, cancellationToken);

        if (property is null || property.IsDeleted)
            throw new NotFoundException("Property was not found.");

        if (!request.IsAdmin && property.OwnerId != request.RequestingUserId)
            throw new ForbiddenException("Only the property owner or an administrator can confirm availability.");

        property.ConfirmStillAvailable();

        _repository.Update(property);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
