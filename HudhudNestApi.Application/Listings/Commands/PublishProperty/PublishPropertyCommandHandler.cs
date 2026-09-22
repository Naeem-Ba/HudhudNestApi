using MediatR;
using FluentValidation.Results;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Events;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings;

namespace HudhudNestApi.Application.Listings.Commands.PublishProperty;

public sealed class PublishPropertyCommandHandler
    : IRequestHandler<PublishPropertyCommand>
{
    private readonly IPropertyRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public PublishPropertyCommandHandler(
        IPropertyRepository repository,
        IUnitOfWork unitOfWork,
        IPublisher publisher)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
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

        // Fire-and-notify: Listings has no idea SocialDistribution (or anyone else) is
        // listening, and never will — see PropertyPublishedEvent's remarks. Any handler-side
        // failure (e.g. the automatic distribution engine erroring out) is swallowed by that
        // handler itself and must never surface here — publishing a property always succeeds
        // once the two lines above have committed, regardless of what downstream listeners do
        // with the news (spec §9: "فشل توزيع المنشورات لا يجب أن يجعل نشر العقار نفسه يفشل").
        await _publisher.Publish(
            new PropertyPublishedEvent(property.Id, now, request.IsAdmin ? null : request.RequestingUserId),
            cancellationToken);
    }
}
