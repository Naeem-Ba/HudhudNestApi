using MediatR;
using HudhudNestApi.Application.Growth;
using HudhudNestApi.Domain.Growth.Entities;
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
    private readonly IGrowthEventRecorder _growth;

    public AddFavoriteCommandHandler(IFavoriteRepository favorites, IUnitOfWork uow, IGrowthEventRecorder? growth = null)
    {
        _favorites = favorites;
        _uow = uow;
        _growth = growth ?? NullGrowthEventRecorder.Instance;
    }

    public async Task<FavoriteMutationResult> Handle(
        AddFavoriteCommand request,
        CancellationToken cancellationToken)
    {
        var propertyExists = await _favorites.PropertyVisibleToAsync(request.PropertyId, request.UserId, cancellationToken);
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
        // Once per user and listing, even if it is removed and saved again.
        await _growth.RecordAsync(GrowthEventNames.FavoriteAdded, $"favorite_added:{request.UserId:N}:{request.PropertyId:N}",
            request.UserId, request.PropertyId, ct: cancellationToken);
        return FavoriteMutationResult.Success("Added to favorites.");
    }
}

