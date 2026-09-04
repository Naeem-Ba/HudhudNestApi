using MediatR;
using PropertyApi.Application.Investments.Commands.PublishInvestmentProject;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjectReview;

/// <summary>Full admin review screen: project detail regardless of status, plus the exact
/// publish-readiness checklist from PublishInvestmentProjectCommandHandler so staff see what's
/// missing before attempting to publish.</summary>
public sealed class GetInvestmentProjectReviewQueryHandler
    : IRequestHandler<GetInvestmentProjectReviewQuery, InvestmentProjectReviewDto?>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IInvestmentProjectFinancialsRepository _financials;
    private readonly IInvestmentRiskAssessmentRepository _risk;
    private readonly IInvestmentDocumentRepository _documents;

    public GetInvestmentProjectReviewQueryHandler(
        IInvestmentProjectRepository projects,
        IInvestmentProjectFinancialsRepository financials,
        IInvestmentRiskAssessmentRepository risk,
        IInvestmentDocumentRepository documents)
    {
        _projects = projects;
        _financials = financials;
        _risk = risk;
        _documents = documents;
    }

    public async Task<InvestmentProjectReviewDto?> Handle(GetInvestmentProjectReviewQuery request, CancellationToken ct)
    {
        var project = await _projects.GetDetailsForAdminAsync(request.Id, ct);
        if (project is null)
            return null;

        // Staff-only field, absent from the public-safe InvestmentProjectDetailsDto — read from
        // the tracked entity rather than adding it to the shared public projection.
        var projectEntity = await _projects.GetByIdAsync(request.Id, ct);
        var rejectionReason = projectEntity?.RejectionReason;

        var financialsEntity = await _financials.GetByProjectIdAsync(request.Id, ct);
        var financialsSummary = financialsEntity is null ? null : await _financials.GetSummaryAsync(request.Id, ct);
        var riskDto = await _risk.GetDtoAsync(request.Id, ct);

        var publicDocuments = await _documents.GetPublicDocumentsAsync(request.Id, ct);
        var publicDocumentTypes = publicDocuments.Select(d => d.DocumentType).ToHashSet();
        var hasRequiredDocuments = PublishInvestmentProjectCommandHandler.RequiredPublicDocumentTypes
            .All(publicDocumentTypes.Contains);

        var hasTitleAndDescription = !string.IsNullOrWhiteSpace(project.Title) && !string.IsNullOrWhiteSpace(project.Description);
        var hasFinancials = financialsEntity is not null;
        var hasRisk = riskDto is not null;

        var readiness = new InvestmentProjectPublishReadinessDto(
            hasTitleAndDescription,
            hasFinancials,
            hasRisk,
            hasRequiredDocuments,
            IsReady: hasTitleAndDescription && hasFinancials && hasRisk && hasRequiredDocuments);

        return new InvestmentProjectReviewDto(
            project,
            rejectionReason,
            financialsSummary,
            riskDto,
            publicDocuments.Count,
            readiness);
    }
}
