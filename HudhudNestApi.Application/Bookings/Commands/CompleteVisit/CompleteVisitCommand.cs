using MediatR;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Bookings.Commands.CompleteVisit;

public sealed record CompleteVisitCommand(
    Guid VisitId,
    Guid OwnerId) : IRequest<bool>;

public sealed class CompleteVisitCommandHandler : IRequestHandler<CompleteVisitCommand, bool>
{
    private readonly IVisitRepository _visits;
    private readonly IUnitOfWork _uow;
    private readonly IPropertyReadRepository _properties;

    public CompleteVisitCommandHandler(
        IVisitRepository visits,
        IUnitOfWork uow,
        IPropertyReadRepository properties)
    {
        _visits = visits;
        _uow = uow;
        _properties = properties;
    }

    public async Task<bool> Handle(CompleteVisitCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetByIdAsync(request.VisitId, ct)
            ?? throw new NotFoundException($"Visit {request.VisitId} was not found.");

        var property = await _properties.GetByIdAsync(visit.PropertyId, ct)
            ?? throw new NotFoundException("Property was not found.");

        if (property.OwnerId != request.OwnerId)
        {
            throw new ForbiddenException("Only the property owner can complete this visit.");
        }

        visit.Complete();
        await _uow.SaveChangesAsync(ct);

        return true;
    }
}
