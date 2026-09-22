using MediatR;
using HudhudNestApi.Application.Favorites.DTOs;

namespace HudhudNestApi.Application.Favorites.Commands.AddFavorite;

public sealed record AddFavoriteCommand(
    Guid UserId,
    Guid PropertyId) : IRequest<FavoriteMutationResult>;

