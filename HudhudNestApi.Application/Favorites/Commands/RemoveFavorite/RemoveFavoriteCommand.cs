using MediatR;
using HudhudNestApi.Application.Favorites.DTOs;

namespace HudhudNestApi.Application.Favorites.Commands.RemoveFavorite;

public sealed record RemoveFavoriteCommand(
    Guid UserId,
    Guid PropertyId) : IRequest<FavoriteMutationResult>;

