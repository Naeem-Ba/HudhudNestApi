using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Search.Interfaces;

namespace PropertyApi.Application.Search.Commands.DeleteSavedSearch;

public sealed class DeleteSavedSearchCommandHandler
    : IRequestHandler<DeleteSavedSearchCommand, bool>
{
    private readonly ISavedSearchRepository _repo;
    private readonly IUnitOfWork _uow;

    public DeleteSavedSearchCommandHandler(ISavedSearchRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<bool> Handle(DeleteSavedSearchCommand request, CancellationToken cancellationToken)
    {
        var savedSearch = await _repo.GetAsync(request.Id, cancellationToken);

        // Ownership check: never let a user delete someone else's saved search,
        // and don't leak existence via a 403 vs 404 distinction — just report "not found".
        if (savedSearch is null || savedSearch.UserId != request.UserId)
            return false;

        _repo.Remove(savedSearch);
        await _uow.SaveChangesAsync(cancellationToken);

        return true;
    }
}
