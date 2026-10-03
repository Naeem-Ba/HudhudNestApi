using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Application.Investments.Commands.AddInvestmentDocument;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Tests.Investments;

/// <summary>
/// Security audit 2026-10-03, finding F-07: investment documents were accepted on the client-declared
/// Content-Type alone (every other upload also checks the extension and the file's magic bytes).
/// </summary>
public sealed class AddInvestmentDocumentSecurityTests
{
    private static readonly byte[] Pdf = "%PDF-1.7\n1 0 obj"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] WindowsExecutable = "MZ\x90\x00\x03\x00\x00\x00"u8.ToArray();
    private static readonly byte[] Html = "<html><script>alert(1)</script></html>"u8.ToArray();

    [Theory]
    [InlineData("application/pdf", "payload.pdf", "exe")]
    [InlineData("image/png", "payload.png", "html")]
    [InlineData("application/pdf", "payload.exe", "pdf")]   // right bytes, wrong extension
    [InlineData("image/png", "payload.png", "pdf")]         // right extension, bytes of another type
    public async Task Rejects_a_file_whose_bytes_or_extension_do_not_match_the_declared_type(
        string contentType, string fileName, string actualBytes)
    {
        var (sut, storage) = CreateSut();
        var bytes = actualBytes switch { "exe" => WindowsExecutable, "html" => Html, _ => Pdf };

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.Handle(Command(contentType, fileName, bytes), CancellationToken.None));

        storage.Verify(x => x.UploadImageAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("application/pdf", "plan.pdf")]
    [InlineData("image/png", "site.PNG")]
    public async Task Accepts_a_genuine_document(string contentType, string fileName)
    {
        var (sut, _) = CreateSut();
        var bytes = contentType == "application/pdf" ? Pdf : Png;

        var id = await sut.Handle(Command(contentType, fileName, bytes), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    private static AddInvestmentDocumentCommand Command(string contentType, string fileName, byte[] bytes) =>
        new(Guid.NewGuid(), InvestmentDocumentType.ProjectPlan,
            new InvestmentDocumentUploadFileDto(new MemoryStream(bytes), fileName, contentType, bytes.Length),
            IsPublic: false);

    private static (AddInvestmentDocumentCommandHandler Sut, Mock<IMediaStorageService> Storage) CreateSut()
    {
        var project = InvestmentProject.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Project", "Description", InvestmentProjectType.Residential);

        var projects = new Mock<IInvestmentProjectRepository>();
        projects.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var storage = new Mock<IMediaStorageService>();
        storage
            .Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Success("https://media.example/doc", "doc-public-id"));

        var folders = new Mock<IMediaFolderBuilder>();
        folders
            .Setup(x => x.BuildFolder(It.IsAny<HudhudNestApi.Application.Common.Enums.MediaEntityType>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns("folder");

        var sut = new AddInvestmentDocumentCommandHandler(
            projects.Object,
            Mock.Of<IInvestmentDocumentRepository>(),
            storage.Object,
            folders.Object,
            Mock.Of<IUnitOfWork>());

        return (sut, storage);
    }
}
