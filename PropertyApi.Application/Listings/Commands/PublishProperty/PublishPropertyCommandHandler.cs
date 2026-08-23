using MediatR;
using FluentValidation.Results;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings;

namespace PropertyApi.Application.Listings.Commands.PublishProperty;

public sealed class PublishPropertyCommandHandler
    : IRequestHandler<PublishPropertyCommand>
{
    private readonly IPropertyRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public PublishPropertyCommandHandler(
        IPropertyRepository repository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(
        PublishPropertyCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _repository.GetByIdWithDetailsAsync(
            request.PropertyId,
            cancellationToken);

        if (property is null || property.IsDeleted)
            throw new NotFoundException("Property was not found.");

        if (!request.IsAdmin && property.OwnerId != request.RequestingUserId)
            throw new ForbiddenException("Only the property owner or an administrator can publish this property.");

        if (property.IsPublished)
            return;

        if (property.Images.All(image => image.IsDeleted))
            throw new ValidationException(
            [
                new ValidationFailure(
                    "Images",
                    "At least one property image is required before publishing.")
            ]);

        var now = DateTime.UtcNow;
        property.Publish();

        // Publication period now comes from ListingLifecyclePolicy so the scheduler, the
        // extension flow, and this handler cannot disagree about how long "3 months" is.
        if (property.ExpiresAt is null || property.ExpiresAt <= now)
            property.ExpiresAt = now.Add(ListingLifecyclePolicy.PublicationPeriod);

        _repository.Update(property);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
