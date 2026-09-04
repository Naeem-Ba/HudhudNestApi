using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Domain.Investments.Entities;

/// <summary>
/// A progress update ("Construction Started", "Milestone", …) posted against an
/// <see cref="InvestmentProject"/>, shown on its public timeline. Published immediately on
/// creation — Phase 1 has no separate draft/publish workflow for updates (spec §10 does not ask
/// for one; only projects and documents have an explicit publish gate).
/// </summary>
public sealed class InvestmentUpdate : AuditableEntity
{
    public Guid InvestmentProjectId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public InvestmentUpdateType UpdateType { get; private set; }
    public DateTime PublishedAt { get; private set; }

    private InvestmentUpdate() { }

    public static InvestmentUpdate Create(
        Guid investmentProjectId,
        string title,
        string content,
        InvestmentUpdateType updateType,
        Guid createdByUserId)
    {
        if (investmentProjectId == Guid.Empty)
            throw new DomainException("مشروع الاستثمار مطلوب.");
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("عنوان التحديث مطلوب.");
        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException("محتوى التحديث مطلوب.");

        return new InvestmentUpdate
        {
            InvestmentProjectId = investmentProjectId,
            Title = title.Trim(),
            Content = content.Trim(),
            UpdateType = updateType,
            PublishedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId,
        };
    }
}
