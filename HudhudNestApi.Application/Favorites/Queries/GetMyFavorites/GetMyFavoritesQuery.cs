using MediatR;
using HudhudNestApi.Application.Favorites.DTOs;

namespace HudhudNestApi.Application.Favorites.Queries.GetMyFavorites;

public sealed record GetMyFavoritesQuery(Guid UserId) : IRequest<IReadOnlyList<FavoriteDto>>;

