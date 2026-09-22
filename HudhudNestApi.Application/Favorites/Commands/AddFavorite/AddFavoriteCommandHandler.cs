using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Favorites.DTOs;
using HudhudNestApi.Application.Favorites.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Favorites.Commands.AddFavorite;

public sealed class AddFavoriteCommandHandler
    : IRequestHandler<AddFavoriteCommand, FavoriteMutationResult>
{
    private readonly IFavoriteRepository _favorites;
    private readonly IUnitOfWork _uow;

    public AddFavoriteCommandHandler(IFavoriteRepository favorites, IUnitOfWork uow)
    {
        _favorites = favorites;
        _uow = uow;
    }

    public async Task<FavoriteMutationResult> Handle(
        AddFavoriteCommand request,
        CancellationToken cancellationToken)
    {
        var propertyExists = await _favorites.PropertyExistsAsync(request.PropertyId, cancellationToken);
        if (!propertyExists)
            return FavoriteMutationResult.NotFound("Property not found.");

        var exists = await _favorites.ExistsAsync(
            request.UserId,
            request.PropertyId,
            cancellationToken);

        if (exists)
            return FavoriteMutationResult.Conflict("Property already in favorites.");

        _favorites.Add(new Favorite
        {
            UserId = request.UserId,
            PropertyId = request.PropertyId,
            CreatedAt = DateTime.UtcNow
        });

        await _uow.SaveChangesAsync(cancellationToken);
        return FavoriteMutationResult.Success("Added to favorites.");
    }
}

