using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Favorites.DTOs;
using HudhudNestApi.Application.Favorites.Interfaces;

namespace HudhudNestApi.Application.Favorites.Commands.RemoveFavorite;

public sealed class RemoveFavoriteCommandHandler
    : IRequestHandler<RemoveFavoriteCommand, FavoriteMutationResult>
{
    private readonly IFavoriteRepository _favorites;
    private readonly IUnitOfWork _uow;

    public RemoveFavoriteCommandHandler(IFavoriteRepository favorites, IUnitOfWork uow)
    {
        _favorites = favorites;
        _uow = uow;
    }

    public async Task<FavoriteMutationResult> Handle(
        RemoveFavoriteCommand request,
        CancellationToken cancellationToken)
    {
        var favorite = await _favorites.GetAsync(
            request.UserId,
            request.PropertyId,
            cancellationToken);

        if (favorite is null)
            return FavoriteMutationResult.NotFound();

        _favorites.Remove(favorite);
        await _uow.SaveChangesAsync(cancellationToken);

        return FavoriteMutationResult.Success();
    }
}

