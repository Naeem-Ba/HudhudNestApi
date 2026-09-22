using Microsoft.Extensions.Logging;
using Moq;
using HudhudNestApi.Application.Common.Enums;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Application.Common.Services;
using HudhudNestApi.Application.Users.Commands.UploadUserAvatar;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Users.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Users;

/// <summary>
/// تغطي دورة رفع الصورة الشخصية كاملة (المشكلة المُبلَّغ عنها: الصورة تختفي بعد
/// تسجيل خروج/دخول جديد لأن الرابط لا يُقرأ/يُحفظ بشكل صحيح) — تحديدًا:
///   - مجلد Cloudinary المستقل ("user-avatars") لا يختلط بمجلد صور الإعلانات.
///   - الرابط المُعاد (secure_url عبر MediaUploadResult.Url) هو ما يُحفَظ في
///     UserAccount.ProfileImageUrl فعليًا (لا الكائن كاملاً).
///   - فشل الحفظ في قاعدة البيانات بعد نجاح الرفع لا يُفقِد الصورة القديمة ولا
///     يترك ملفًا يتيمًا في Cloudinary بلا داعٍ (الإصلاح المضاف هنا).
/// </summary>
public sealed class UploadUserAvatarCommandHandlerTests
{
    private static UserAccount CreateAccount(Guid id) =>
        UserAccount.Create(id, "Test", "User", DateTime.UtcNow);

    private static UploadUserAvatarFileDto ValidPngFile()
    {
        // PNG magic bytes (0x89 'P' 'N' 'G' \r \n \x1A \n) + filler — HasValidImageSignatureAsync
        // only inspects the header, so anything after it is irrelevant to validation.
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        var stream = new MemoryStream(bytes);
        return new UploadUserAvatarFileDto(stream, "avatar.png", "image/png", bytes.Length);
    }

    private static (
        Mock<IUserAccountRepository> Accounts,
        Mock<IMediaStorageService> Storage,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<ILogger<UploadUserAvatarCommandHandler>> Logger,
        UploadUserAvatarCommandHandler Handler)
        CreateSut(UserAccount account)
    {
        var accounts = new Mock<IUserAccountRepository>();
        accounts
            .Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var storage = new Mock<IMediaStorageService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var logger = new Mock<ILogger<UploadUserAvatarCommandHandler>>();

        var handler = new UploadUserAvatarCommandHandler(
            accounts.Object,
            storage.Object,
            new MediaFolderBuilder(),
            unitOfWork.Object,
            logger.Object);

        return (accounts, storage, unitOfWork, logger, handler);
    }

    [Fact]
    public async Task Handle_UploadsToTheUsersOwnFolder_NotThePropertyImagesFolder()
    {
        var account = CreateAccount(Guid.NewGuid());
        var (_, storage, unitOfWork, _, handler) = CreateSut(account);

        string? capturedFolder = null;
        storage
            .Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, string, CancellationToken>(
                (_, _, _, folder, _) => capturedFolder = folder)
            .ReturnsAsync(MediaUploadResult.Success(
                "https://res.cloudinary.com/demo/image/upload/v1/user-avatars/abc123.png",
                "user-avatars/abc123"));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await handler.Handle(
            new UploadUserAvatarCommand(account.Id, ValidPngFile()), CancellationToken.None);

        var expectedFolder = new MediaFolderBuilder()
            .BuildFolder(MediaEntityType.User, account.Id, MediaCategories.Profile);

        Assert.Equal(UploadUserAvatarStatus.Success, result.Status);
        Assert.Equal(expectedFolder, capturedFolder);
        Assert.DoesNotContain("properties", capturedFolder);
    }

    [Fact]
    public async Task Handle_OnSuccess_SavesOnlyTheSecureUrlString_NotTheWholeUploadObject()
    {
        var account = CreateAccount(Guid.NewGuid());
        var (_, storage, unitOfWork, _, handler) = CreateSut(account);

        const string secureUrl = "https://res.cloudinary.com/demo/image/upload/v1/user-avatars/abc123.png";
        storage
            .Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Success(secureUrl, "user-avatars/abc123"));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await handler.Handle(
            new UploadUserAvatarCommand(account.Id, ValidPngFile()), CancellationToken.None);

        Assert.Equal(UploadUserAvatarStatus.Success, result.Status);
        Assert.Equal(secureUrl, result.ImageUrl);
        // This is the field GetCurrentUserQueryHandler/GetUserProfileQueryHandler read back —
        // proving it holds exactly the URL string is what guarantees the image survives a
        // fresh GET /users/me after logout/login, not just the in-memory command result.
        Assert.Equal(secureUrl, account.ProfileImageUrl);
        Assert.Equal("user-avatars/abc123", account.ProfileImagePublicId);
    }

    [Fact]
    public async Task Handle_ReplacingAnExistingAvatar_DeletesTheOldOneFromStorage()
    {
        var account = CreateAccount(Guid.NewGuid());
        account.UpdateProfileImage(
            "https://res.cloudinary.com/demo/image/upload/v1/user-avatars/old.png",
            "user-avatars/old",
            DateTime.UtcNow);

        var (_, storage, unitOfWork, _, handler) = CreateSut(account);

        storage
            .Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Success(
                "https://res.cloudinary.com/demo/image/upload/v1/user-avatars/new.png",
                "user-avatars/new"));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await handler.Handle(
            new UploadUserAvatarCommand(account.Id, ValidPngFile()), CancellationToken.None);

        storage.Verify(x => x.DeleteImageAsync("user-avatars/old", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("user-avatars/new", account.ProfileImagePublicId);
    }

    [Fact]
    public async Task Handle_UserWithNoExistingAvatar_DoesNotAttemptToDeleteAnything()
    {
        var account = CreateAccount(Guid.NewGuid());
        var (_, storage, unitOfWork, _, handler) = CreateSut(account);

        storage
            .Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Success(
                "https://res.cloudinary.com/demo/image/upload/v1/user-avatars/new.png",
                "user-avatars/new"));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await handler.Handle(
            new UploadUserAvatarCommand(account.Id, ValidPngFile()), CancellationToken.None);

        storage.Verify(
            x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCloudinaryUploadFails_ReturnsStorageFailed_AndAccountIsUnchanged()
    {
        var account = CreateAccount(Guid.NewGuid());
        var (_, storage, unitOfWork, _, handler) = CreateSut(account);

        storage
            .Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Failed("Cloudinary is unavailable."));

        var result = await handler.Handle(
            new UploadUserAvatarCommand(account.Id, ValidPngFile()), CancellationToken.None);

        Assert.Equal(UploadUserAvatarStatus.StorageFailed, result.Status);
        Assert.Null(account.ProfileImageUrl);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSavingTheUrlFails_CleansUpTheOrphanedUploadAndReturnsStorageFailed()
    {
        // The exact "upload succeeded, saving the link failed" case called out by the bug
        // report — must not leave the user thinking it worked, and must not leave the file
        // sitting in Cloudinary forever with nothing pointing at it.
        var account = CreateAccount(Guid.NewGuid());
        var (_, storage, unitOfWork, _, handler) = CreateSut(account);

        storage
            .Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Success(
                "https://res.cloudinary.com/demo/image/upload/v1/user-avatars/new.png",
                "user-avatars/new"));
        unitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB connection lost"));

        var result = await handler.Handle(
            new UploadUserAvatarCommand(account.Id, ValidPngFile()), CancellationToken.None);

        Assert.Equal(UploadUserAvatarStatus.StorageFailed, result.Status);
        storage.Verify(x => x.DeleteImageAsync("user-avatars/new", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    public async Task Handle_RejectsDisallowedContentTypes_WithoutCallingStorage(string contentType)
    {
        var account = CreateAccount(Guid.NewGuid());
        var (_, storage, _, _, handler) = CreateSut(account);

        var bytes = new byte[] { 1, 2, 3, 4 };
        var file = new UploadUserAvatarFileDto(
            new MemoryStream(bytes), "avatar.gif", contentType, bytes.Length);

        var result = await handler.Handle(
            new UploadUserAvatarCommand(account.Id, file), CancellationToken.None);

        Assert.Equal(UploadUserAvatarStatus.ValidationFailed, result.Status);
        storage.Verify(
            x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_RejectsOversizedFiles()
    {
        var account = CreateAccount(Guid.NewGuid());
        var (_, _, _, _, handler) = CreateSut(account);

        var file = new UploadUserAvatarFileDto(
            new MemoryStream(new byte[8]), "avatar.png", "image/png", 3_000_000);

        var result = await handler.Handle(
            new UploadUserAvatarCommand(account.Id, file), CancellationToken.None);

        Assert.Equal(UploadUserAvatarStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Handle_UnknownAccount_ReturnsNotFound()
    {
        var accounts = new Mock<IUserAccountRepository>();
        accounts
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        var handler = new UploadUserAvatarCommandHandler(
            accounts.Object,
            Mock.Of<IMediaStorageService>(),
            new MediaFolderBuilder(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<ILogger<UploadUserAvatarCommandHandler>>());

        var result = await handler.Handle(
            new UploadUserAvatarCommand(Guid.NewGuid(), ValidPngFile()), CancellationToken.None);

        Assert.Equal(UploadUserAvatarStatus.NotFound, result.Status);
    }
}
