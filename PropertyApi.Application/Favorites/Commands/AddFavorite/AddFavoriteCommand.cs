using MediatR;
using PropertyApi.Application.Favorites.DTOs;

namespace PropertyApi.Application.Favorites.Commands.AddFavorite;

public sealed record AddFavoriteCommand(
    Guid UserId,
    Guid PropertyId) : IRequest<FavoriteMutationResult>;

