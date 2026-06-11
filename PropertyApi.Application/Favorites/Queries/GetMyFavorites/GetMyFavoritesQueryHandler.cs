using MediatR;
using PropertyApi.Application.Favorites.DTOs;
using PropertyApi.Application.Favorites.Interfaces;

namespace PropertyApi.Application.Favorites.Queries.GetMyFavorites;

public sealed class GetMyFavoritesQueryHandler
    : IRequestHandler<GetMyFavoritesQuery, IReadOnlyList<FavoriteDto>>
{
    private readonly IFavoriteRepository _favorites;

    public GetMyFavoritesQueryHandler(IFavoriteRepository favorites)
        => _favorites = favorites;

    public Task<IReadOnlyList<FavoriteDto>> Handle(
        GetMyFavoritesQuery request,
        CancellationToken cancellationToken)
    {
        return _favorites.GetByUserIdAsync(request.UserId, cancellationToken);
    }
}
