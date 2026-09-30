using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Investments.Commands.PublishInvestmentProject;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Tests.Investments;

/// <summary>
/// Publish-readiness is a cross-aggregate check the InvestmentProject entity itself cannot make
/// (Phase 1 spec §17) — these tests exercise that checklist at the handler level.
/// </summary>
public sealed class PublishInvestmentProjectCommandHandlerTests
{
    private static InvestmentProject ScheduledProject()
    {
        var project = InvestmentProject.Create(
            Guid.NewGuid(), Guid.NewGuid(), "مشروع جاهز للنشر", "وصف كافٍ", InvestmentProjectType.Residential, "USD");
        project.SubmitForReview();
        project.Approve();
        project.Schedule(null);
        return project;
    }

    private static Mock<IInvestmentProjectRepository> ProjectsReturning(InvestmentProject project)
    {
        var repo = new Mock<IInvestmentProjectRepository>();
        repo.Setup(x => x.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        return repo;
    }

    private static IReadOnlyList<InvestmentDocumentDto> AllRequiredDocuments() =>
        PublishInvestmentProjectCommandHandler.RequiredPublicDocumentTypes
            .Select(type => new InvestmentDocumentDto(Guid.NewGuid(), type, "file.pdf", 1, DateTime.UtcNow, "https://example.com/file.pdf", IsPublic: true))
            .ToList();

    /// <summary>Same required types, but never marked public — must still fail readiness. This is
    /// the exact scenario a real HTTP integration test (Phase 2) caught the handler getting
    /// wrong when it queried through the Published-status-gated repository method instead.</summary>
    private static IReadOnlyList<InvestmentDocumentDto> AllRequiredDocumentsButPrivate() =>
        PublishInvestmentProjectCommandHandler.RequiredPublicDocumentTypes
            .Select(type => new InvestmentDocumentDto(Guid.NewGuid(), type, "file.pdf", 1, null, "https://example.com/file.pdf", IsPublic: false))
            .ToList();

    private PublishInvestmentProjectCommandHandler BuildHandler(
        InvestmentProject project,
        InvestmentProjectFinancials? financials,
        InvestmentRiskAssessment? risk,
        IReadOnlyList<InvestmentDocumentDto> documents)
    {
        var financialsRepo = new Mock<IInvestmentProjectFinancialsRepository>();
        financialsRepo.Setup(x => x.GetByProjectIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(financials);

        var riskRepo = new Mock<IInvestmentRiskAssessmentRepository>();
        riskRepo.Setup(x => x.GetByProjectIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(risk);

        var documentsRepo = new Mock<IInvestmentDocumentRepository>();
        documentsRepo.Setup(x => x.GetAllDocumentsForAdminAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(documents);

        return new PublishInvestmentProjectCommandHandler(
            ProjectsReturning(project).Object,
            financialsRepo.Object,
            riskRepo.Object,
            documentsRepo.Object,
            Mock.Of<IUnitOfWork>());
    }

    [Fact]
    public async Task Publish_Throws_WhenFinancialsMissing()
    {
        var project = ScheduledProject();
        var handler = BuildHandler(project, financials: null, risk: MakeRiskAssessment(project.Id), AllRequiredDocuments());

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new PublishInvestmentProjectCommand(project.Id), default));
    }

    [Fact]
    public async Task Publish_Throws_WhenRiskAssessmentMissing()
    {
        var project = ScheduledProject();
        var handler = BuildHandler(project, financials: MakeFinancials(project.Id), risk: null, AllRequiredDocuments());

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new PublishInvestmentProjectCommand(project.Id), default));
    }

    [Fact]
    public async Task Publish_Throws_WhenRequiredDocumentsMissing()
    {
        var project = ScheduledProject();
        var handler = BuildHandler(
            project, MakeFinancials(project.Id), MakeRiskAssessment(project.Id), documents: Array.Empty<InvestmentDocumentDto>());

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new PublishInvestmentProjectCommand(project.Id), default));
    }

    [Fact]
    public async Task Publish_Throws_WhenRequiredDocumentsExistButAreNotPublic()
    {
        var project = ScheduledProject();
        var handler = BuildHandler(
            project, MakeFinancials(project.Id), MakeRiskAssessment(project.Id), AllRequiredDocumentsButPrivate());

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new PublishInvestmentProjectCommand(project.Id), default));
    }

    [Fact]
    public async Task Publish_Succeeds_WhenEverythingIsPresent()
    {
        var project = ScheduledProject();
        var handler = BuildHandler(project, MakeFinancials(project.Id), MakeRiskAssessment(project.Id), AllRequiredDocuments());

        await handler.Handle(new PublishInvestmentProjectCommand(project.Id), default);

        Assert.Equal(InvestmentProjectStatus.Published, project.Status);
    }

    [Fact]
    public async Task Publish_Throws_NotFound_WhenProjectMissing()
    {
        var repo = new Mock<IInvestmentProjectRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((InvestmentProject?)null);

        var handler = new PublishInvestmentProjectCommandHandler(
            repo.Object,
            Mock.Of<IInvestmentProjectFinancialsRepository>(),
            Mock.Of<IInvestmentRiskAssessmentRepository>(),
            Mock.Of<IInvestmentDocumentRepository>(),
            Mock.Of<IUnitOfWork>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new PublishInvestmentProjectCommand(Guid.NewGuid()), default));
    }

    private static InvestmentProjectFinancials MakeFinancials(Guid projectId) => InvestmentProjectFinancials.Create(projectId);

    private static InvestmentRiskAssessment MakeRiskAssessment(Guid projectId) =>
        InvestmentRiskAssessment.Create(
            projectId, InvestmentRiskLevel.Medium, InvestmentRiskLevel.Medium, InvestmentRiskLevel.Low,
            InvestmentRiskLevel.Medium, InvestmentRiskLevel.Low, InvestmentRiskLevel.Medium, 40, "ملخص المخاطر.");
}
