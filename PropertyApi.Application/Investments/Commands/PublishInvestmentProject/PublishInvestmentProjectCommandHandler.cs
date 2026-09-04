using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.Commands.PublishInvestmentProject;

/// <summary>
/// Publishes a Scheduled project — but only once every cross-aggregate readiness rule from
/// Phase 1 spec §17 is satisfied. The InvestmentProject aggregate cannot see its sibling
/// aggregates (financials/risk/documents), so these checks live here rather than inside
/// InvestmentProject.Publish().
/// </summary>
public sealed class PublishInvestmentProjectCommandHandler : IRequestHandler<PublishInvestmentProjectCommand>
{
    /// <summary>
    /// Explicitly-coded minimum document set a project must have publicly attached before it can
    /// go live — Phase 1 spec §17 requires this be defined in code, not just a comment.
    /// </summary>
    public static readonly IReadOnlyCollection<InvestmentDocumentType> RequiredPublicDocumentTypes =
        [InvestmentDocumentType.ProjectPlan, InvestmentDocumentType.FinancialStatement];

    private readonly IInvestmentProjectRepository _projects;
    private readonly IInvestmentProjectFinancialsRepository _financials;
    private readonly IInvestmentRiskAssessmentRepository _risk;
    private readonly IInvestmentDocumentRepository _documents;
    private readonly IUnitOfWork _uow;

    public PublishInvestmentProjectCommandHandler(
        IInvestmentProjectRepository projects,
        IInvestmentProjectFinancialsRepository financials,
        IInvestmentRiskAssessmentRepository risk,
        IInvestmentDocumentRepository documents,
        IUnitOfWork uow)
    {
        _projects = projects;
        _financials = financials;
        _risk = risk;
        _documents = documents;
        _uow = uow;
    }

    public async Task Handle(PublishInvestmentProjectCommand request, CancellationToken ct)
    {
        var project = await _projects.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException($"Investment project {request.Id} was not found.");

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(project.Title) || string.IsNullOrWhiteSpace(project.Description))
            missing.Add("عنوان أو وصف المشروع");

        var financials = await _financials.GetByProjectIdAsync(project.Id, ct);
        if (financials is null)
            missing.Add("البيانات المالية للمشروع");

        var riskAssessment = await _risk.GetByProjectIdAsync(project.Id, ct);
        if (riskAssessment is null)
            missing.Add("تقييم المخاطر");

        var publicDocuments = await _documents.GetPublicDocumentsAsync(project.Id, ct);
        var publicDocumentTypes = publicDocuments.Select(d => d.DocumentType).ToHashSet();
        var missingDocumentTypes = RequiredPublicDocumentTypes
            .Where(required => !publicDocumentTypes.Contains(required))
            .ToList();
        if (missingDocumentTypes.Count > 0)
            missing.Add($"المستندات المطلوبة ({string.Join(", ", missingDocumentTypes)})");

        if (missing.Count > 0)
        {
            throw new DomainException(
                $"لا يمكن نشر المشروع — العناصر التالية ناقصة: {string.Join(" | ", missing)}.");
        }

        project.Publish();
        await _uow.SaveChangesAsync(ct);
    }
}
