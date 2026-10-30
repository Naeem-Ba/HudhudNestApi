using HudhudNestApi.Application.Investments.Commands.PublishInvestmentProject;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Investments.Services;

/// <summary>
/// QA Phase 10, P10-D10. The publish gate demands a public ProjectPlan and FinancialStatement, but nothing stopped
/// an admin from deleting or hiding the last one afterwards, leaving a live project without the documents its
/// publication was conditioned on. A replacement must be uploaded (and made public) first.
/// </summary>
public static class RequiredDocumentGuard
{
    private static readonly InvestmentProjectStatus[] GuardedStatuses =
    [
        InvestmentProjectStatus.Scheduled, InvestmentProjectStatus.Published, InvestmentProjectStatus.Suspended,
    ];

    public static async Task EnsureNotLastRequiredAsync(
        IInvestmentProjectRepository projects,
        IInvestmentDocumentRepository documents,
        Guid projectId,
        Guid documentId,
        CancellationToken ct)
    {
        var project = await projects.GetByIdAsync(projectId, ct);
        if (project is null || !GuardedStatuses.Contains(project.Status))
            return;

        var all = await documents.GetAllDocumentsForAdminAsync(projectId, ct);
        var target = all.FirstOrDefault(d => d.Id == documentId);
        if (target is null || !target.IsPublic ||
            !PublishInvestmentProjectCommandHandler.RequiredPublicDocumentTypes.Contains(target.DocumentType))
            return;

        var hasAnotherPublicOfType = all.Any(d =>
            d.Id != documentId && d.IsPublic && d.DocumentType == target.DocumentType);

        if (!hasAnotherPublicOfType)
        {
            throw new DomainException(
                $"لا يمكن حذف أو إخفاء المستند المطلوب ({target.DocumentType}) من مشروع مجدول أو منشور. " +
                "ارفع مستندًا بديلًا من النوع نفسه وأتحه للعامة أولًا.");
        }
    }
}
