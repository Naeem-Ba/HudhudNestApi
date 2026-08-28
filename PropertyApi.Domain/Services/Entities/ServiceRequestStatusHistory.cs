using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Domain.Services.Entities;

/// <summary>
/// Append-only audit trail row for one ServiceRequest status transition. Never updated or
/// deleted after creation — CreatedAt (from BaseEntity) doubles as "when this transition
/// happened," so there is no separate ChangedAt field to keep in sync with it.
/// </summary>
public sealed class ServiceRequestStatusHistory : BaseEntity
{
    private ServiceRequestStatusHistory() { }

    public Guid ServiceRequestId { get; private set; }

    /// <summary>Null for the very first row (request creation has no "from" state).</summary>
    public ServiceRequestStatus? FromStatus { get; private set; }

    public ServiceRequestStatus ToStatus { get; private set; }

    /// <summary>Who caused this transition. Null for a system-generated row.</summary>
    public Guid? ChangedByUserId { get; private set; }

    public string? Note { get; private set; }

    public static ServiceRequestStatusHistory Record(
        Guid serviceRequestId,
        ServiceRequestStatus? fromStatus,
        ServiceRequestStatus toStatus,
        Guid? changedByUserId,
        string? note,
        DateTime utcNow)
    {
        return new ServiceRequestStatusHistory
        {
            ServiceRequestId = serviceRequestId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ChangedByUserId = changedByUserId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }
}
