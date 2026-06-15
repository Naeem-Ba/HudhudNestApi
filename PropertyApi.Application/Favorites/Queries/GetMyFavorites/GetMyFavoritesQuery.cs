using MediatR;
using PropertyApi.Application.Favorites.DTOs;

namespace PropertyApi.Application.Favorites.Queries.GetMyFavorites;

public sealed record GetMyFavoritesQuery(Guid UserId) : IRequest<IReadOnlyList<FavoriteDto>>;

