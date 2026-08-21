using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Reviews.DTOs;
using PropertyApi.Application.Reviews.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Reviews.Entities;

namespace PropertyApi.Application.Reviews.Commands.RateUser;

public sealed class RateUserCommandHandler
    : IRequestHandler<RateUserCommand, UserRatingDto>
{
    private readonly IUserRatingRepository _ratings;
    private readonly IUserAccountRepository _accounts;
    private readonly IVisitRepository _visits;
    private readonly IMessageRepository _messages;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<RateUserCommandHandler> _logger;

    public RateUserCommandHandler(
        IUserRatingRepository ratings,
        IUserAccountRepository accounts,
        IVisitRepository visits,
        IMessageRepository messages,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<RateUserCommandHandler> logger)
    {
        _ratings = ratings;
        _accounts = accounts;
        _visits = visits;
        _messages = messages;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<UserRatingDto> Handle(
        RateUserCommand request, CancellationToken ct)
    {
        // ── الفحص 1: المستخدم المُقيَّم موجود ────────────────────────────
        var ratedAccount = await _accounts.GetByIdAsync(request.RatedUserId, ct)
            ?? throw new NotFoundException("المستخدم المُراد تقييمه غير موجود.");

        // ── الفحص 2: لا يمكنك تقييم نفسك ─────────────────────────────────
        // نفس الفحص موجود في UserRating.Create (Domain) كخط دفاع ثانٍ،
        // لكن التحقق هنا يعطي رسالة خطأ واضحة قبل أي عمل إضافي.
        if (request.RatedUserId == request.RaterId)
            throw new DomainException("لا يمكنك تقييم نفسك.");

        // ── الفحص 3: يجب إتمام زيارة أو وجود تواصل عبر الرسائل قبل التقييم ──
        // هذا يمنع التقييمات العشوائية من مستخدمين لم يتفاعلوا فعليًا مع
        // المُعلن، ويطابق طلب المستخدم صراحة (شرط زيارة مكتملة أو مراسلة).
        var hasCompletedVisit = await _visits.HasCompletedVisitWithOwnerAsync(
            request.RaterId, request.RatedUserId, ct);

        var isEligible = hasCompletedVisit ||
            await _messages.HasAnyConversationAsync(request.RaterId, request.RatedUserId, ct);

        if (!isEligible)
            throw new DomainException(
                "يجب إتمام زيارة لأحد عقارات هذا المستخدم أو التواصل معه عبر الرسائل قبل أن تتمكّن من تقييمه.");

        // ── إنشاء التقييم أو تعديله (Upsert) ─────────────────────────────
        // يسمح للمستخدم بتعديل تقييمه السابق بدل رفض المحاولة الثانية
        // برسالة "لقد قمت بتقييم هذا المستخدم مسبقاً" كما كان سابقاً.
        var existingRating = await _ratings.GetByRaterAndRatedAsync(
            request.RatedUserId, request.RaterId, ct);

        var isNewRating = existingRating is null;
        UserRating rating;

        if (existingRating is null)
        {
            rating = UserRating.Create(
                request.RatedUserId,
                request.RaterId,
                request.Credibility,
                request.Safety,
                request.ResponseSpeed,
                request.Transparency,
                request.Comment);

            await _ratings.AddAsync(rating, ct);
        }
        else
        {
            existingRating.Update(
                request.Credibility,
                request.Safety,
                request.ResponseSpeed,
                request.Transparency,
                request.Comment,
                DateTime.UtcNow);

            _ratings.Update(existingRating);
            rating = existingRating;
        }

        await _uow.SaveChangesAsync(ct);

        var raterAccount = await _accounts.GetByIdAsync(request.RaterId, ct);
        var raterName = BuildDisplayName(raterAccount);

        /*
         * إشعار توصيل التقييم للمستخدم المُقيَّم غير حرج — التقييم نفسه
         * محفوظ بالفعل في السطر أعلاه. نلفّه بـ try/catch من البداية
         * (بعكس ما حصل سابقاً في RequestVisitCommandHandler وAddReviewCommandHandler
         * حيث كان هذا الاستدعاء بدون حماية ويُسقط الطلب بأكمله عند فشل الإشعار).
         *
         * نُرسل الإشعار فقط عند تقييم جديد — تعديل تقييم موجود لا يُنشئ إشعارًا
         * جديدًا كي لا نُغرق المُقيَّم بإشعارات متكررة عن نفس المُقيِّم.
         */
        if (isNewRating)
        {
            try
            {
                await _notifications.NotifyUserRatedAsync(
                    recipientId: request.RatedUserId,
                    raterId: request.RaterId,
                    raterName: raterName,
                    overallScore: rating.OverallScore,
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to create/send user-rated notification. " +
                    "RatingId={RatingId}, RatedUserId={RatedUserId}, RaterId={RaterId}",
                    rating.Id,
                    request.RatedUserId,
                    request.RaterId);
            }
        }

        return new UserRatingDto(
            Id: rating.Id,
            RatedUserId: rating.RatedUserId,
            RaterId: rating.RaterId,
            RaterName: raterName,
            RaterImageUrl: raterAccount?.ProfileImageUrl,
            Credibility: rating.Credibility,
            Safety: rating.Safety,
            ResponseSpeed: rating.ResponseSpeed,
            Transparency: rating.Transparency,
            OverallScore: rating.OverallScore,
            Comment: rating.Comment,
            CreatedAt: rating.CreatedAt);
    }

    private static string BuildDisplayName(Domain.Users.Entities.UserAccount? account)
    {
        if (account is null)
            return "مستخدم";

        if (!string.IsNullOrWhiteSpace(account.DisplayName))
            return account.DisplayName;

        var fullName = $"{account.FirstName} {account.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "مستخدم" : fullName;
    }
}
