using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Domain.SocialDistribution.Entities;

/// <summary>
/// Append-only audit trail row for one SocialPublication status transition (spec §10: "تسجيل كل
/// انتقال مهم"). Never updated or deleted — mirrors
/// <c>HudhudNestApi.Domain.Services.Entities.ServiceRequestStatusHistory</c> exactly. CreatedAt
/// (from BaseEntity) doubles as "when this transition happened".
/// </summary>
public sealed class SocialPublicationStatusHistory : BaseEntity
{
    private SocialPublicationStatusHistory() { }

    public Guid SocialPublicationId { get; private set; }

    /// <summary>Null for the very first row (creation has no "from" state).</summary>
    public SocialPublicationStatus? FromStatus { get; private set; }

    public SocialPublicationStatus ToStatus { get; private set; }

    /// <summary>Who/what caused this transition. Null for a system/worker-driven row.</summary>
    public Guid? ChangedByUserId { get; private set; }

    /// <summary>Free-text note — e.g. the sanitized error message on a Failed transition. Never raw exception detail.</summary>
    public string? Note { get; private set; }

    public static SocialPublicationStatusHistory Record(
        Guid socialPublicationId,
        SocialPublicationStatus? fromStatus,
        SocialPublicationStatus toStatus,
        Guid? changedByUserId,
        string? note,
        DateTime utcNow)
    {
        return new SocialPublicationStatusHistory
        {
            SocialPublicationId = socialPublicationId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ChangedByUserId = changedByUserId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }
}
