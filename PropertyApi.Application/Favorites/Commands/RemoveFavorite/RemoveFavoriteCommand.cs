using MediatR;
using PropertyApi.Application.Favorites.DTOs;

namespace PropertyApi.Application.Favorites.Commands.RemoveFavorite;

public sealed record RemoveFavoriteCommand(
    Guid UserId,
    Guid PropertyId) : IRequest<FavoriteMutationResult>;

