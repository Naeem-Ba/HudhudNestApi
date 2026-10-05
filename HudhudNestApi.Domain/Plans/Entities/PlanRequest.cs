using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Plans.Enums;

namespace HudhudNestApi.Domain.Plans.Entities;

/// <summary>
/// A user's request for a paid plan, waiting for an administrator to confirm payment.
///
/// There is no payment gateway, so a paid plan cannot activate itself: if choosing one set
/// UserAccount.PlanId straight away, every paid tier would be free for anyone who called the
/// endpoint. This record is the honest middle state — it says what was asked for and by
/// whom, grants no benefit, and is resolved only by an administrator (approve → the plan is
/// activated through UserAccount.ActivatePlanByAdmin; reject → nothing changes). Same
/// Pending-then-confirm shape as the featured-listing fee (ConfirmFeaturedListingPayment).
/// </summary>
public sealed class PlanRequest : BaseEntity
{
    private PlanRequest() { }

    public Guid UserId { get; private set; }

    public Guid PlanId { get; private set; }

    public PlanRequestStatus Status { get; private set; } = PlanRequestStatus.Pending;

    /// <summary>The administrator who approved or rejected it. Null while Pending or Superseded.</summary>
    public Guid? ResolvedByUserId { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    /// <summary>Why it was rejected, or a payment reference note on approval. Optional.</summary>
    public string? ResolutionNote { get; private set; }

    public static PlanRequest Create(Guid userId, Guid planId, DateTime utcNow)
    {
        if (userId == Guid.Empty)
            throw new DomainException("User id is required.");

        if (planId == Guid.Empty)
            throw new DomainException("Plan id is required.");

        return new PlanRequest
        {
            UserId = userId,
            PlanId = planId,
            Status = PlanRequestStatus.Pending,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    public void Approve(Guid adminUserId, string? note, DateTime utcNow)
        => Resolve(PlanRequestStatus.Approved, adminUserId, note, utcNow);

    public void Reject(Guid adminUserId, string? note, DateTime utcNow)
        => Resolve(PlanRequestStatus.Rejected, adminUserId, note, utcNow);

    /// <summary>The user changed their mind: a newer request replaces this pending one.</summary>
    public void Supersede(DateTime utcNow)
    {
        EnsurePending();
        Status = PlanRequestStatus.Superseded;
        ResolvedAt = utcNow;
        UpdatedAt = utcNow;
    }

    private void Resolve(PlanRequestStatus status, Guid adminUserId, string? note, DateTime utcNow)
    {
        EnsurePending();

        if (adminUserId == Guid.Empty)
            throw new DomainException("Resolving administrator id is required.");

        Status = status;
        ResolvedByUserId = adminUserId;
        ResolvedAt = utcNow;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        UpdatedAt = utcNow;
    }

    private void EnsurePending()
    {
        if (Status != PlanRequestStatus.Pending)
            throw new DomainException($"لا يمكن تغيير طلب حالته {Status}.");
    }
}
