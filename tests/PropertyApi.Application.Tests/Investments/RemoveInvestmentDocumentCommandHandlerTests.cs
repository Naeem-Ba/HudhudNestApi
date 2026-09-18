using Microsoft.Extensions.Logging;
using Moq;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.Commands.RemoveInvestmentDocument;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.Investments.Enums;
using Xunit;

namespace PropertyApi.Application.Tests.Investments;

/// <summary>
/// Covers the orphan-asset blind spot fixed alongside the media-folder redesign: this handler
/// used to swallow a failed Cloudinary delete with a bare `catch { }` and no log line, so a
/// storage-side failure here left an asset nobody could ever find again. It must still not fail
/// the request (the metadata row is the source of truth and is already gone), but it must now
/// log the failure.
/// </summary>
public sealed class RemoveInvestmentDocumentCommandHandlerTests
{
    private static InvestmentDocument CreateDocument(Guid investmentProjectId) =>
        InvestmentDocument.Create(
            investmentProjectId,
            InvestmentDocumentType.ProjectPlan,
            "brochure.pdf",
            "Cloudinary",
            "investments/proj-1/documents/brochure",
            "https://cdn.example.com/brochure.pdf",
            documentHash: null,
            isPublic: false);

    [Fact]
    public async Task Handle_WhenStorageDeleteFails_DoesNotThrow_ButLogsAWarning()
    {
        var projectId = Guid.NewGuid();
        var document = CreateDocument(projectId);

        var documents = new Mock<IInvestmentDocumentRepository>();
        documents.Setup(x => x.GetByIdAsync(document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var storage = new Mock<IMediaStorageService>();
        storage.Setup(x => x.DeleteImageAsync(document.StorageKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cloudinary is unavailable."));

        var uow = new Mock<IUnitOfWork>();
        var logger = new Mock<ILogger<RemoveInvestmentDocumentCommandHandler>>();

        var handler = new RemoveInvestmentDocumentCommandHandler(
            documents.Object, storage.Object, uow.Object, logger.Object);

        // Must not throw — the metadata row is already removed and is the source of truth.
        await handler.Handle(
            new RemoveInvestmentDocumentCommand(projectId, document.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(document.IsDeleted);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenStorageDeleteSucceeds_DoesNotLogAWarning()
    {
        var projectId = Guid.NewGuid();
        var document = CreateDocument(projectId);

        var documents = new Mock<IInvestmentDocumentRepository>();
        documents.Setup(x => x.GetByIdAsync(document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var storage = new Mock<IMediaStorageService>();
        storage.Setup(x => x.DeleteImageAsync(document.StorageKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var uow = new Mock<IUnitOfWork>();
        var logger = new Mock<ILogger<RemoveInvestmentDocumentCommandHandler>>();

        var handler = new RemoveInvestmentDocumentCommandHandler(
            documents.Object, storage.Object, uow.Object, logger.Object);

        await handler.Handle(
            new RemoveInvestmentDocumentCommand(projectId, document.Id, Guid.NewGuid()), CancellationToken.None);

        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
