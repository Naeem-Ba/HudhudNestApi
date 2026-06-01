using MediatR;
using WohnungenApi.Application.Common.Interfaces;
using WohnungenApi.Application.Listings.Interfaces;

namespace WohnungenApi.Application.Listings.Commands.DeleteProperty;

/// <summary>
/// BUG FIX: Was an empty "internal class DeletePropertyCommandHandler {}" — completely missing.
/// </summary>
public sealed class DeletePropertyCommandHandler
    : IRequestHandler<DeletePropertyCommand, bool>
{
    private readonly IPropertyRepository _repo;
    private readonly IUnitOfWork _uow;

    public DeletePropertyCommandHandler(IPropertyRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<bool> Handle(
        DeletePropertyCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _repo.GetByIdAsync(request.PropertyId, cancellationToken);

        if (property is null)
            return false;

        if (property.OwnerId != request.RequestingUserId)
            throw new UnauthorizedAccessException(
                "You are not authorized to delete this listing.");

        // Domain method stamps IsDeleted=true, DeletedAt, DeletedByUserId
        property.MarkAsDeleted(request.RequestingUserId);

        // AppDbContext.SaveChangesAsync() intercepts EntityState.Deleted
        // and converts it to soft-delete automatically.
        // Calling Remove() here is fine — the DbContext handles conversion.
        _repo.Remove(property);
        await _uow.SaveChangesAsync(cancellationToken);

        return true;
    }
}