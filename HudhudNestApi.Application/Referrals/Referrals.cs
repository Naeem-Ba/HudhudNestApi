using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Domain.Referrals.Entities;
using FluentValidation;
using MediatR;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Referrals;

/// <summary>
/// Growth engine P5 settings (section "Referrals"). Every value is a commercial choice with a cautious default
/// (DECISIONS.md D-11). No reward is granted automatically: an admin records it by hand.
/// </summary>
public sealed class ReferralOptions
{
    public const string SectionName = "Referrals";

    /// <summary>Off: codes are not accepted at sign-up and the job does nothing. Existing rows stay.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The invited account must have a confirmed phone number.</summary>
    public bool RequireConfirmedPhone { get; set; } = true;

    /// <summary>The invited account must have a listing that has stayed on the market this many days.</summary>
    public int QualifyingListingDays { get; set; } = 7;

    /// <summary>A referral not qualified within this many days expires.</summary>
    public int WindowDays { get; set; } = 90;

    /// <summary>Qualified referrals one referrer can collect per 30 days; above it they are rejected.</summary>
    public int MaxQualifiedPerReferrerPer30Days { get; set; } = 10;

    /// <summary>Shown to users on the invite page; null means no reward is announced.</summary>
    public string? RewardDescriptionAr { get; set; }
    public string? RewardDescriptionEn { get; set; }
    public string? RewardDescriptionDe { get; set; }
}

public sealed record MyReferralItemDto(DateTime JoinedAt, string Status, string? Reason, bool Rewarded);

/// <summary>What a referrer sees: counts and statuses only, never who signed up.</summary>
public sealed record MyReferralsDto(
    string Code,
    int Pending,
    int Qualified,
    int Rejected,
    int Expired,
    int QualifyingListingDays,
    bool RequireConfirmedPhone,
    string? RewardAr,
    string? RewardEn,
    string? RewardDe,
    IReadOnlyList<MyReferralItemDto> Items);

public sealed record AdminReferralDto(
    Guid Id,
    Guid ReferrerUserId,
    string ReferrerName,
    Guid ReferredUserId,
    string ReferredName,
    string Code,
    string Status,
    string? Reason,
    DateTime CreatedAt,
    DateTime? DecidedAt,
    DateTime? RewardedAt);

public sealed record AdminReferralPage(IReadOnlyList<AdminReferralDto> Items, int Total);

/// <summary>Storage and reads for referrals. Implemented in Infrastructure.</summary>
public interface IReferralStore
{
    Task<ReferralCode> GetOrCreateCodeAsync(Guid userId, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// Links a new account to the code's owner. Silently does nothing for an unknown code, a code of the same
    /// account, or an account that was already referred. Never throws: sign-up must not fail because of it.
    /// </summary>
    Task AttachAtRegistrationAsync(Guid newUserId, string? code, DateTime nowUtc, CancellationToken ct);

    Task<IReadOnlyList<Referral>> GetByReferrerAsync(Guid referrerUserId, int take, CancellationToken ct);
    Task<AdminReferralPage> GetAdminPageAsync(ReferralStatus? status, int page, int pageSize, CancellationToken ct);
    Task<Referral?> GetAsync(Guid id, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}

public sealed record GetMyReferralsQuery(Guid UserId) : IRequest<MyReferralsDto>;

public sealed record GetAdminReferralsQuery(ReferralStatus? Status, int Page = 1, int PageSize = 20) : IRequest<AdminReferralPage>;

public sealed record RejectReferralCommand(Guid Id, Guid AdminUserId, string Reason) : IRequest<bool>;

public sealed record MarkReferralRewardedCommand(Guid Id, Guid AdminUserId) : IRequest<bool>;

public sealed class ReferralHandlers :
    IRequestHandler<GetMyReferralsQuery, MyReferralsDto>,
    IRequestHandler<GetAdminReferralsQuery, AdminReferralPage>,
    IRequestHandler<RejectReferralCommand, bool>,
    IRequestHandler<MarkReferralRewardedCommand, bool>
{
    private const int MaxListed = 100;

    private readonly IReferralStore _store;
    private readonly ReferralOptions _options;
    private readonly TimeProvider _clock;
    private readonly IAuditLogService? _audit;

    public ReferralHandlers(IReferralStore store, Microsoft.Extensions.Options.IOptions<ReferralOptions> options, TimeProvider clock,
        IAuditLogService? audit = null)
    {
        _audit = audit;
        _store = store;
        _options = options.Value;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<MyReferralsDto> Handle(GetMyReferralsQuery q, CancellationToken ct)
    {
        var code = await _store.GetOrCreateCodeAsync(q.UserId, Now, ct);
        var rows = await _store.GetByReferrerAsync(q.UserId, MaxListed, ct);
        return new MyReferralsDto(
            code.Code,
            rows.Count(r => r.Status == ReferralStatus.Pending),
            rows.Count(r => r.Status == ReferralStatus.Qualified),
            rows.Count(r => r.Status == ReferralStatus.Rejected),
            rows.Count(r => r.Status == ReferralStatus.Expired),
            _options.QualifyingListingDays,
            _options.RequireConfirmedPhone,
            _options.RewardDescriptionAr,
            _options.RewardDescriptionEn,
            _options.RewardDescriptionDe,
            rows.Select(r => new MyReferralItemDto(r.CreatedAt, r.Status.ToString(), r.Reason, r.RewardedAt is not null)).ToList());
    }

    public Task<AdminReferralPage> Handle(GetAdminReferralsQuery q, CancellationToken ct) =>
        _store.GetAdminPageAsync(q.Status, Math.Max(1, q.Page), Math.Clamp(q.PageSize, 1, 100), ct);

    public async Task<bool> Handle(RejectReferralCommand c, CancellationToken ct)
    {
        var referral = await _store.GetAsync(c.Id, ct);
        if (referral is null)
            return false;
        if (referral.RewardedAt is not null)
            throw new ConflictException("Reward already recorded; it cannot be rejected now.", "REFERRAL_ALREADY_REWARDED");
        var before = referral.Status.ToString();
        referral.Reject("admin:" + (c.Reason ?? string.Empty).Trim(), Now);
        await _store.SaveAsync(ct);
        if (_audit is not null)
            await _audit.LogAsync(c.AdminUserId, "referral.reject", null, $"{{\"id\":\"{referral.Id}\",\"status\":\"{before}\"}}",
                $"{{\"id\":\"{referral.Id}\",\"status\":\"Rejected\"}}", ct);
        return true;
    }

    public async Task<bool> Handle(MarkReferralRewardedCommand c, CancellationToken ct)
    {
        var referral = await _store.GetAsync(c.Id, ct);
        if (referral is null)
            return false;
        if (referral.Status != ReferralStatus.Qualified)
            throw new ConflictException("Only a qualified referral can be marked as rewarded.", "REFERRAL_NOT_QUALIFIED");
        referral.MarkRewarded(c.AdminUserId, Now);
        await _store.SaveAsync(ct);
        if (_audit is not null)
            await _audit.LogAsync(c.AdminUserId, "referral.rewarded", null, null, $"{{\"id\":\"{referral.Id}\"}}", ct);
        return true;
    }
}

public sealed class RejectReferralCommandValidator : AbstractValidator<RejectReferralCommand>
{
    public RejectReferralCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
    }
}
