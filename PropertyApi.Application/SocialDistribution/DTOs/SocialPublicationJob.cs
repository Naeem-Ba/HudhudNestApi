using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.DTOs;

/// <summary>
/// The "queue message" the Worker consumes (Phase 6 spec §10) — a read-only projection of a due
/// <c>SocialPublication</c>, not a separately-persisted row (see <c>ISocialPublicationJobQueue</c>
/// remarks for why: the Publication row already IS the durable state; a second table tracking
/// "is this due" in parallel would be a second source of truth that could drift from it).
/// <see cref="JobId"/> equals <see cref="PublicationId"/> 1:1 — deliberately: this codebase's
/// design has never allowed a job to cover more than one publication, so a separate identity
/// would carry no information the PublicationId doesn't already have.
/// </summary>
public sealed record SocialPublicationJob(
    Guid JobId,
    Guid PublicationId,
    Guid SocialAccountId,
    SocialPlatform Platform,
    string IdempotencyKey,
    int Attempt,
    DateTime CreatedAt);
