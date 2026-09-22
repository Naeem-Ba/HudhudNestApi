namespace HudhudNestApi.Domain.SocialDistribution.Enums;

/// <summary>
/// The SocialPublication state machine (spec §10). Enforced exclusively inside
/// <see cref="Entities.SocialPublication"/> via EnsureStatus-guarded methods, mirroring
/// ServiceRequest/VisitRequest's pattern elsewhere in this codebase.
///
/// Draft      → Queued | Cancelled
/// Queued     → Publishing | Cancelled
/// Publishing → Published | Failed
/// Failed     → Retrying | Cancelled
/// Retrying   → Publishing | Cancelled
/// Published  → (terminal — never returns to Draft/Queued/Publishing)
/// Cancelled  → (terminal)
/// </summary>
public enum SocialPublicationStatus
{
    Draft = 1,
    Queued = 2,
    Publishing = 3,
    Published = 4,
    Failed = 5,
    Retrying = 6,
    Cancelled = 7,
}
