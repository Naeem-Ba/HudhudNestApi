using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Favorites.Commands.AddFavorite;
using PropertyApi.Application.Favorites.Commands.RemoveFavorite;
using PropertyApi.Application.Favorites.DTOs;
using PropertyApi.Application.Favorites.Interfaces;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Auth.Tests.Application.Handlers;

public sealed class FavoritesHandlerTests
{
    [Fact(DisplayName = "Add favorite handler rejects duplicate favorite")]
    public async Task AddFavorite_Duplicate_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();

        var repository = new Mock<IFavoriteRepository>();
        repository.Setup(x => x.PropertyExistsAsync(propertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repository.Setup(x => x.ExistsAsync(userId, propertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new AddFavoriteCommandHandler(repository.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new AddFavoriteCommand(userId, propertyId), CancellationToken.None);

        Assert.Equal(FavoriteMutationStatus.Conflict, result.Status);
        repository.Verify(x => x.Add(It.IsAny<Favorite>()), Times.Never);
    }

    [Fact(DisplayName = "Remove favorite handler removes entity and commits through UnitOfWork")]
    public async Task RemoveFavorite_Existing_Removes_And_Commits()
    {
        var userId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var favorite = new Favorite { UserId = userId, PropertyId = propertyId };

        var repository = new Mock<IFavoriteRepository>();
        repository.Setup(x => x.GetAsync(userId, propertyId, It.IsAny<CancellationToken>())).ReturnsAsync(favorite);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new RemoveFavoriteCommandHandler(repository.Object, uow.Object);

        var result = await handler.Handle(new RemoveFavoriteCommand(userId, propertyId), CancellationToken.None);

        Assert.Equal(FavoriteMutationStatus.Success, result.Status);
        repository.Verify(x => x.Remove(favorite), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
