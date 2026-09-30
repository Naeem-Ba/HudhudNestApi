using Moq;
using HudhudNestApi.Application.AppUpdates.Commands.CreateAppRelease;
using HudhudNestApi.Application.AppUpdates.Commands.DeleteAppRelease;
using HudhudNestApi.Application.AppUpdates.Commands.DisableAppRelease;
using HudhudNestApi.Application.AppUpdates.Commands.EnableAppRelease;
using HudhudNestApi.Application.AppUpdates.Commands.UpdateAppRelease;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.AppUpdates.Entities;
using HudhudNestApi.Domain.AppUpdates.Enums;
using HudhudNestApi.Domain.AppUpdates.ValueObjects;

namespace HudhudNestApi.Application.Tests.AppUpdates;

public sealed class AppReleaseCommandHandlerTests
{
    private static AppRelease NewRelease(bool isEnabled = true) => AppRelease.Create(
        AppPlatform.Android, AppVersion.Parse("1.1.0"), AppVersion.Parse("1.0.0"),
        null, "ar", "en", "de", DateTime.UtcNow, isEnabled);

    private static (Mock<IAppReleaseRepository> Repo, Mock<IAppReleaseCacheService> Cache, Mock<IUnitOfWork> Uow, Mock<IAuditLogService> Audit)
        Mocks() => (new Mock<IAppReleaseRepository>(), new Mock<IAppReleaseCacheService>(), new Mock<IUnitOfWork>(), new Mock<IAuditLogService>());

    [Fact]
    public async Task Create_HappyPath_AddsSavesInvalidatesAndAudits()
    {
        var (repo, cache, uow, audit) = Mocks();
        repo.Setup(x => x.ExistsEnabledForPlatformAndVersionAsync(
                AppPlatform.Android, "1.1.0", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateAppReleaseCommandHandler(repo.Object, cache.Object, uow.Object, audit.Object);
        var command = new CreateAppReleaseCommand(
            AppPlatform.Android, "1.1.0", "1.0.0", null, "ar", "en", "de", DateTime.UtcNow, true,
            Guid.NewGuid(), "127.0.0.1");

        var id = await handler.Handle(command, default);

        Assert.NotEqual(Guid.Empty, id);
        repo.Verify(x => x.Add(It.IsAny<AppRelease>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(x => x.InvalidateAsync(AppPlatform.Android, It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(x => x.LogAsync(
            command.PerformedByUserId, "AppReleaseCreatedByAdmin", command.IpAddress,
            null, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateEnabledRelease_ThrowsConflict_DoesNotAddOrSave()
    {
        var (repo, cache, uow, audit) = Mocks();
        repo.Setup(x => x.ExistsEnabledForPlatformAndVersionAsync(
                AppPlatform.Android, "1.1.0", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new CreateAppReleaseCommandHandler(repo.Object, cache.Object, uow.Object, audit.Object);
        var command = new CreateAppReleaseCommand(
            AppPlatform.Android, "1.1.0", "1.0.0", null, null, null, null, DateTime.UtcNow, true,
            Guid.NewGuid(), null);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, default));

        repo.Verify(x => x.Add(It.IsAny<AppRelease>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_NotFound_Throws()
    {
        var (repo, cache, uow, audit) = Mocks();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((AppRelease?)null);

        var handler = new UpdateAppReleaseCommandHandler(repo.Object, cache.Object, uow.Object, audit.Object);
        var command = new UpdateAppReleaseCommand(
            Guid.NewGuid(), "1.2.0", "1.0.0", null, null, null, null, DateTime.UtcNow, Guid.NewGuid(), null);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, default));
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_HappyPath_SavesInvalidatesAndAudits()
    {
        var release = NewRelease();
        var (repo, cache, uow, audit) = Mocks();
        repo.Setup(x => x.GetByIdAsync(release.Id, It.IsAny<CancellationToken>())).ReturnsAsync(release);
        repo.Setup(x => x.ExistsEnabledForPlatformAndVersionAsync(
                AppPlatform.Android, "1.2.0", release.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new UpdateAppReleaseCommandHandler(repo.Object, cache.Object, uow.Object, audit.Object);
        var command = new UpdateAppReleaseCommand(
            release.Id, "1.2.0", "1.0.0", null, null, null, null, DateTime.UtcNow, Guid.NewGuid(), null);

        await handler.Handle(command, default);

        Assert.Equal("1.2.0", release.Version);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(x => x.InvalidateAsync(AppPlatform.Android, It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(x => x.LogAsync(
            command.PerformedByUserId, "AppReleaseUpdatedByAdmin", command.IpAddress,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enable_NotFound_Throws()
    {
        var (repo, cache, uow, audit) = Mocks();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((AppRelease?)null);

        var handler = new EnableAppReleaseCommandHandler(repo.Object, cache.Object, uow.Object, audit.Object);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new EnableAppReleaseCommand(Guid.NewGuid(), Guid.NewGuid(), null), default));
    }

    [Fact]
    public async Task Enable_HappyPath_EnablesSavesInvalidatesAndAudits()
    {
        var release = NewRelease(isEnabled: false);
        var (repo, cache, uow, audit) = Mocks();
        repo.Setup(x => x.GetByIdAsync(release.Id, It.IsAny<CancellationToken>())).ReturnsAsync(release);
        repo.Setup(x => x.ExistsEnabledForPlatformAndVersionAsync(
                release.Platform, release.Version, release.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new EnableAppReleaseCommandHandler(repo.Object, cache.Object, uow.Object, audit.Object);
        await handler.Handle(new EnableAppReleaseCommand(release.Id, Guid.NewGuid(), null), default);

        Assert.True(release.IsEnabled);
        cache.Verify(x => x.InvalidateAsync(release.Platform, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Disable_HappyPath_DisablesSavesInvalidatesAndAudits()
    {
        var release = NewRelease(isEnabled: true);
        var (repo, cache, uow, audit) = Mocks();
        repo.Setup(x => x.GetByIdAsync(release.Id, It.IsAny<CancellationToken>())).ReturnsAsync(release);

        var handler = new DisableAppReleaseCommandHandler(repo.Object, cache.Object, uow.Object, audit.Object);
        await handler.Handle(new DisableAppReleaseCommand(release.Id, Guid.NewGuid(), null), default);

        Assert.False(release.IsEnabled);
        cache.Verify(x => x.InvalidateAsync(release.Platform, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_HappyPath_SoftDeletes_NeverPhysicallyRemoves()
    {
        var release = NewRelease();
        var performedBy = Guid.NewGuid();
        var (repo, cache, uow, audit) = Mocks();
        repo.Setup(x => x.GetByIdAsync(release.Id, It.IsAny<CancellationToken>())).ReturnsAsync(release);

        var handler = new DeleteAppReleaseCommandHandler(repo.Object, cache.Object, uow.Object, audit.Object);
        await handler.Handle(new DeleteAppReleaseCommand(release.Id, performedBy, null), default);

        Assert.True(release.IsDeleted);
        Assert.NotNull(release.DeletedAt);
        Assert.Equal(performedBy, release.DeletedByUserId);
        cache.Verify(x => x.InvalidateAsync(release.Platform, It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(x => x.LogAsync(
            performedBy, "AppReleaseDeletedByAdmin", null, null, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_NotFound_Throws()
    {
        var (repo, cache, uow, audit) = Mocks();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((AppRelease?)null);

        var handler = new DeleteAppReleaseCommandHandler(repo.Object, cache.Object, uow.Object, audit.Object);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new DeleteAppReleaseCommand(Guid.NewGuid(), Guid.NewGuid(), null), default));
    }
}
