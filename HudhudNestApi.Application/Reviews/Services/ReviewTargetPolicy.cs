using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;

namespace HudhudNestApi.Application.Reviews.Services;

/// <summary>
/// Who may be reviewed (owner decision 2026-10-18): not every account, only one whose plan is in force right
/// now. Reviews exist to guide guests and buyers towards live advertisers; an account with no plan or an
/// expired one has nothing on offer, so a new review of it would be noise. Reviews already written stay.
/// One rule for the whole product: sale/rent property reviews, user ratings and short-stay reviews all ask it
/// (service reviews ask the provider's own standing instead -- providers hold no plan).
/// </summary>
public interface IReviewTargetPolicy
{
    /// <summary>Throws when <paramref name="targetUserId"/> cannot receive a new review.</summary>
    Task EnsureReviewableAsync(Guid targetUserId, CancellationToken ct = default);

    /// <summary>The same question without throwing, for the eligibility endpoints.</summary>
    Task<bool> IsReviewableAsync(Guid targetUserId, CancellationToken ct = default);
}

public sealed class ReviewTargetPolicy : IReviewTargetPolicy
{
    public const string ReviewTargetInactiveMessage =
        "لا يمكن تقييم هذا الحساب حالياً لأن صاحبه ليست لديه خطة فعّالة.";

    private readonly IUserAccountRepository _accounts;

    public ReviewTargetPolicy(IUserAccountRepository accounts) => _accounts = accounts;

    public async Task EnsureReviewableAsync(Guid targetUserId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(targetUserId, ct)
            ?? throw new NotFoundException("الحساب المراد تقييمه غير موجود.");

        if (!account.HasActivePlanBenefits(DateTime.UtcNow))
            throw new DomainException(ReviewTargetInactiveMessage);
    }

    public async Task<bool> IsReviewableAsync(Guid targetUserId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(targetUserId, ct);
        return account is not null && account.HasActivePlanBenefits(DateTime.UtcNow);
    }
}
