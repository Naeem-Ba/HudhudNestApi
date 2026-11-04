using Moq;
using HudhudNestApi.Application.Analytics.Queries.GetMarketInsights;
using HudhudNestApi.Application.Common.Enums;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Application.Demand.Commands;
using HudhudNestApi.Application.Investments.Commands.AddInvestmentDocument;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Application.Services.Commands.UploadServiceRequestDocument;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Commands.GenerateSocialMediaAsset;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.Agencies;

/// <summary>QA Phase 18 fixes that live in the application layer: D01, D02, D06, D09.</summary>
public sealed class Phase18FixTests
{
    // ── D01: an empty property id is a 422, not a 500 ────────────

    [Fact]
    public void ProposeListing_EmptyIds_AreInvalid_RealIdsAreValid()
    {
        var validator = new ProposeListingCommandValidator();

        Assert.False(validator.Validate(new ProposeListingCommand(Guid.NewGuid(), Guid.Empty)).IsValid);
        Assert.False(validator.Validate(new ProposeListingCommand(Guid.Empty, Guid.NewGuid())).IsValid);
        Assert.True(validator.Validate(new ProposeListingCommand(Guid.NewGuid(), Guid.NewGuid())).IsValid);
    }

    // ── D02: an unknown property on the asset endpoint is a 404 ──

    [Fact]
    public async Task GenerateAsset_UnknownProperty_BecomesNotFound()
    {
        var generator = new Mock<ISocialMediaAssetGenerator>();
        generator
            .Setup(g => g.GenerateAsync(It.IsAny<GenerateSocialAssetRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SocialAssetGenerationException("العقار غير موجود", retryable: false, propertyNotFound: true));
        var handler = new GenerateSocialMediaAssetCommandHandler(generator.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new GenerateSocialMediaAssetCommand(Guid.NewGuid(), SocialPlatform.Facebook, "ar", [], "t", "b", null), default));
    }

    [Fact]
    public async Task GenerateAsset_OtherGenerationFailures_KeepTheirOwnType()
    {
        var generator = new Mock<ISocialMediaAssetGenerator>();
        generator
            .Setup(g => g.GenerateAsync(It.IsAny<GenerateSocialAssetRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SocialAssetGenerationException("فشل الرفع", retryable: true));
        var handler = new GenerateSocialMediaAssetCommandHandler(generator.Object);

        await Assert.ThrowsAsync<SocialAssetGenerationException>(() => handler.Handle(
            new GenerateSocialMediaAssetCommand(Guid.NewGuid(), SocialPlatform.Facebook, "ar", [], "t", "b", null), default));
    }

    // ── D09: control characters never reach the database ─────────

    [Theory]
    [InlineData(null, true)]
    [InlineData("SY", true)]
    [InlineData("SYR", true)]
    [InlineData("S\0Y", false)]
    [InlineData("%00", true)] // the literal text, not a NUL: harmless
    [InlineData("S\u0007Y", false)]
    [InlineData("S\nY", false)]
    [InlineData("ABCDEFGHI", false)]
    public void MarketInsights_CountryCode_IsCheckedBeforeTheDatabase(string? code, bool valid)
        => Assert.Equal(valid, new GetMarketInsightsQueryValidator().Validate(new GetMarketInsightsQuery(code)).IsValid);

    // ── D06: no orphan file when the row cannot be saved ─────────

    [Fact]
    public async Task InvestmentDocument_SaveFails_UploadedFileIsDeleted_AndTheErrorSurfaces()
    {
        var storage = StorageReturning("inv-public-id");
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var handler = InvestmentHandler(storage, uow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(InvestmentCommand(), default));

        storage.Verify(s => s.DeleteImageAsync("inv-public-id", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvestmentDocument_SaveSucceeds_FileIsKept()
    {
        var storage = StorageReturning("inv-public-id");
        var handler = InvestmentHandler(storage, new Mock<IUnitOfWork>());

        await handler.Handle(InvestmentCommand(), default);

        storage.Verify(s => s.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvestmentDocument_CompensationFailureDoesNotHideTheOriginalError()
    {
        var storage = StorageReturning("inv-public-id");
        storage.Setup(s => s.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("storage down"));
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InvestmentHandler(storage, uow).Handle(InvestmentCommand(), default));

        Assert.Equal("db down", ex.Message);
    }

    [Fact]
    public async Task ServiceRequestDocument_SaveFails_UploadedFileIsDeleted()
    {
        var requesterId = Guid.NewGuid();
        var providerUserId = Guid.NewGuid();
        var provider = HudhudNestApi.Domain.Services.Entities.ServiceProvider.Create(
            providerUserId, null, "مزوّد", null, null, null, DateTime.UtcNow);
        var request = ServiceRequest.Create(
            "SR-1", Guid.NewGuid(), requesterId, provider.Id, Guid.NewGuid(), ServiceCategory.Verification, null, DateTime.UtcNow);

        var requests = new Mock<IServiceRequestRepository>();
        requests.Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        var providers = new Mock<IServiceProviderRepository>();
        providers.Setup(p => p.GetByIdAsync(provider.Id, It.IsAny<CancellationToken>())).ReturnsAsync(provider);
        var storage = StorageReturning("svc-public-id");
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var handler = new UploadServiceRequestDocumentCommandHandler(
            requests.Object, providers.Object, Mock.Of<IServiceRequestDocumentRepository>(),
            storage.Object, FolderBuilder(), uow.Object);

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UploadServiceRequestDocumentCommand(request.Id, requesterId, new MemoryStream(png), "a.png", "image/png", png.Length),
            default));

        storage.Verify(s => s.DeleteImageAsync("svc-public-id", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── helpers ──────────────────────────────────────────────────

    private static Mock<IMediaStorageService> StorageReturning(string publicId)
    {
        var storage = new Mock<IMediaStorageService>();
        storage.Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Success("https://media.example/doc", publicId));
        return storage;
    }

    private static IMediaFolderBuilder FolderBuilder()
    {
        var folders = new Mock<IMediaFolderBuilder>();
        folders.Setup(x => x.BuildFolder(It.IsAny<MediaEntityType>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns("folder");
        return folders.Object;
    }

    private static AddInvestmentDocumentCommandHandler InvestmentHandler(Mock<IMediaStorageService> storage, Mock<IUnitOfWork> uow)
    {
        var project = InvestmentProject.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Project", "Description", InvestmentProjectType.Residential);
        var projects = new Mock<IInvestmentProjectRepository>();
        projects.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(project);
        return new AddInvestmentDocumentCommandHandler(
            projects.Object, Mock.Of<IInvestmentDocumentRepository>(), storage.Object, FolderBuilder(), uow.Object);
    }

    private static AddInvestmentDocumentCommand InvestmentCommand()
    {
        var bytes = "%PDF-1.7\n1 0 obj"u8.ToArray();
        return new(Guid.NewGuid(), InvestmentDocumentType.ProjectPlan,
            new InvestmentDocumentUploadFileDto(new MemoryStream(bytes), "plan.pdf", "application/pdf", bytes.Length), IsPublic: false);
    }
}
