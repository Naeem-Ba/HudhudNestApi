using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Application.Services.Commands.ServiceOutcome;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Application.Services.ProviderOnboarding;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Admin;

// Everything here is reached only through AdminServiceRequestsController, which carries
// [Authorize(Roles = Admin)] on the class; the acting admin's id always comes from the token.

// ── List ────────────────────────────────────────────────────────────────

public sealed record SearchAdminServiceRequestsQuery(AdminServiceRequestFilter Filter)
    : IRequest<ServicePageDto<AdminServiceRequestSummaryDto>>;

public sealed class SearchAdminServiceRequestsQueryHandler
    : IRequestHandler<SearchAdminServiceRequestsQuery, ServicePageDto<AdminServiceRequestSummaryDto>>
{
    private readonly IServiceRequestAdminRepository _admin;

    public SearchAdminServiceRequestsQueryHandler(IServiceRequestAdminRepository admin) => _admin = admin;

    public async Task<ServicePageDto<AdminServiceRequestSummaryDto>> Handle(
        SearchAdminServiceRequestsQuery request, CancellationToken ct)
    {
        var f = request.Filter with
        {
            Page = Math.Max(1, request.Filter.Page),
            PageSize = Math.Clamp(request.Filter.PageSize, 1, 100),
            Search = string.IsNullOrWhiteSpace(request.Filter.Search) ? null : request.Filter.Search.Trim(),
        };

        var (items, total) = await _admin.SearchAsync(f, DateTime.UtcNow, ct);
        return new(items, total, f.Page, f.PageSize);
    }
}

// ── Statistics ──────────────────────────────────────────────────────────

public sealed record GetServiceStatsQuery(DateTime? From, DateTime? To) : IRequest<ServiceStatsDto>;

public sealed class GetServiceStatsQueryHandler : IRequestHandler<GetServiceStatsQuery, ServiceStatsDto>
{
    private readonly IServiceRequestAdminRepository _admin;

    public GetServiceStatsQueryHandler(IServiceRequestAdminRepository admin) => _admin = admin;

    public Task<ServiceStatsDto> Handle(GetServiceStatsQuery request, CancellationToken ct)
    {
        if (request.From is { } from && request.To is { } to && from > to)
            throw new Domain.Common.Exceptions.DomainException("تاريخ البداية بعد تاريخ النهاية.");

        return _admin.GetStatsAsync(request.From, request.To, ct);
    }
}

// ── Detail ──────────────────────────────────────────────────────────────

public sealed record GetAdminServiceRequestDetailQuery(Guid ServiceRequestId) : IRequest<AdminServiceRequestDetailDto>;

public sealed class GetAdminServiceRequestDetailQueryHandler
    : IRequestHandler<GetAdminServiceRequestDetailQuery, AdminServiceRequestDetailDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IServiceReviewRepository _reviews;
    private readonly IUserAccountRepository _users;
    private readonly IServiceRequestMessageRepository _messages;

    public GetAdminServiceRequestDetailQueryHandler(
        IServiceRequestRepository requests,
        IServiceRequestStatusHistoryRepository history,
        IServiceReviewRepository reviews,
        IUserAccountRepository users,
        IServiceRequestMessageRepository messages)
    {
        _requests = requests;
        _history = history;
        _reviews = reviews;
        _users = users;
        _messages = messages;
    }

    public async Task<AdminServiceRequestDetailDto> Handle(
        GetAdminServiceRequestDetailQuery request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var entries = await _history.GetByServiceRequestIdAsync(serviceRequest.Id, ct);
        var review = await _reviews.GetByRequestIdAsync(serviceRequest.Id, ct);

        AdminServiceReviewDto? reviewDto = null;
        if (review is not null)
        {
            var reviewer = await _users.GetByIdAsync(review.ReviewerId, ct);
            var name = reviewer is null
                ? "—"
                : string.IsNullOrWhiteSpace(reviewer.DisplayName)
                    ? $"{reviewer.FirstName} {reviewer.LastName}".Trim()
                    : reviewer.DisplayName!;
            reviewDto = new AdminServiceReviewDto(
                review.Id, review.ServiceRequestId, name, review.Rating, review.Comment,
                review.IsHidden, review.HiddenReason, review.CreatedAt);
        }

        var thread = await _messages.GetThreadAsync(serviceRequest.Id, ct);
        var stats = new ServiceRequestMessageStats(thread.Count, 0, thread.Count == 0 ? null : thread[^1].CreatedAt);

        return new AdminServiceRequestDetailDto(
            ServiceMapper.ToDto(serviceRequest, ServiceRequestAudience.Admin, stats),
            entries.OrderBy(e => e.CreatedAt).Select(e => ServiceMapper.ToDto(e)).ToList(),
            reviewDto,
            NeedsAttention(serviceRequest, DateTime.UtcNow),
            Commands.ServiceConversation.GetServiceRequestMessagesQueryHandler.ToDtos(
                serviceRequest, serviceRequest.ServiceProvider!, viewer: null, thread));
    }

    /// <summary>Same rule as the list filter: disputed by the customer, or open (an unconfirmed inquiry counts) and untouched for a week.</summary>
    internal static bool NeedsAttention(ServiceRequest r, DateTime utcNow)
    {
        if (r.RequesterAnswer == ServiceRequesterAnswer.Disputed)
            return true;

        var open = r.Status is ServiceRequestStatus.Inquiry or ServiceRequestStatus.UnderReview
            or ServiceRequestStatus.Accepted or ServiceRequestStatus.Scheduled or ServiceRequestStatus.InProgress;

        return open && r.LastActivityAt < utcNow - IServiceRequestAdminRepository.StaleAfter;
    }
}

// ── Set the outcome on behalf of the provider ───────────────────────────

/// <summary>
/// The admin closes a request the provider never closed (or closed wrongly), with a mandatory
/// note of why. It goes through the same domain methods as the provider, so the state rules are
/// the same; the history row and the audit log say it was an admin decision.
/// </summary>
public sealed record AdminSetServiceRequestOutcomeCommand(
    Guid AdminUserId,
    Guid ServiceRequestId,
    bool Completed,
    ServiceOutcomeReason? Reason,
    string? ProviderNote,
    string AdminNote,
    string? IpAddress) : IRequest<AdminServiceRequestDetailDto>;

public sealed class AdminSetServiceRequestOutcomeCommandValidator
    : AbstractValidator<AdminSetServiceRequestOutcomeCommand>
{
    public AdminSetServiceRequestOutcomeCommandValidator()
    {
        RuleFor(x => x.AdminNote).NotEmpty().WithMessage("اكتب سبب القرار الإداري.").MaximumLength(500);
        RuleFor(x => x.ProviderNote).MaximumLength(500).When(x => x.ProviderNote is not null);
        RuleFor(x => x.Reason).NotNull().WithMessage("اختر سبب عدم الإتمام.").When(x => !x.Completed);
        RuleFor(x => x.Reason).IsInEnum().When(x => x.Reason is not null);
    }
}

public sealed class AdminSetServiceRequestOutcomeCommandHandler
    : IRequestHandler<AdminSetServiceRequestOutcomeCommand, AdminServiceRequestDetailDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;
    private readonly INotificationService _notifications;
    private readonly ISender _sender;
    private readonly ILogger<AdminSetServiceRequestOutcomeCommandHandler> _logger;

    public AdminSetServiceRequestOutcomeCommandHandler(
        IServiceRequestRepository requests,
        IServiceRequestStatusHistoryRepository history,
        IUnitOfWork uow,
        IAuditLogService audit,
        INotificationService notifications,
        ISender sender,
        ILogger<AdminSetServiceRequestOutcomeCommandHandler> logger)
    {
        _requests = requests;
        _history = history;
        _uow = uow;
        _audit = audit;
        _notifications = notifications;
        _sender = sender;
        _logger = logger;
    }

    public async Task<AdminServiceRequestDetailDto> Handle(
        AdminSetServiceRequestOutcomeCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var before = serviceRequest.Status;
        var now = DateTime.UtcNow;

        if (request.Completed)
            serviceRequest.Complete(null, null, now, byAdmin: true);
        else
            serviceRequest.MarkNotCompleted(request.Reason!.Value, request.ProviderNote, now, byAdmin: true);

        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                serviceRequest.Id, before, serviceRequest.Status, request.AdminUserId,
                $"قرار إداري: {request.AdminNote}", now),
            ct);

        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(
            request.AdminUserId, AuditActions.ServiceRequestOutcomeSetByAdmin, request.IpAddress,
            JsonSerializer.Serialize(new { requestId = serviceRequest.Id, status = before.ToString() }, JsonSerializerOptions.Web),
            JsonSerializer.Serialize(new
            {
                requestId = serviceRequest.Id,
                status = serviceRequest.Status.ToString(),
                reason = serviceRequest.OutcomeReason?.ToString(),
            }, JsonSerializerOptions.Web),
            ct);

        if (request.Completed)
        {
            await TryNotifyAsync(serviceRequest, NotificationType.ServiceRequestCompleted, NotificationKeys.ServiceCompletedDetail, ct);
        }
        else
        {
            await MarkServiceRequestNotCompletedCommandHandler.NotifyRequesterAsync(
                serviceRequest, _notifications, _logger, ct);
        }

        return await _sender.Send(new GetAdminServiceRequestDetailQuery(serviceRequest.Id), ct);
    }

    private async Task TryNotifyAsync(ServiceRequest r, NotificationType type, string key, CancellationToken ct)
    {
        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: r.RequesterId,
                propertyId: r.PropertyId,
                propertyTitle: r.Property?.Title ?? "-",
                type: type,
                detail: NotificationText.Of(key, ("number", r.RequestNumber)),
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin outcome notification. ServiceRequestId={ServiceRequestId}", r.Id);
        }
    }
}

// ── Hide / unhide a review ──────────────────────────────────────────────

public sealed record AdminSetServiceReviewVisibilityCommand(
    Guid AdminUserId,
    Guid ReviewId,
    bool Hidden,
    string? Reason,
    string? IpAddress) : IRequest<AdminServiceReviewDto>;

public sealed class AdminSetServiceReviewVisibilityCommandValidator
    : AbstractValidator<AdminSetServiceReviewVisibilityCommand>
{
    public AdminSetServiceReviewVisibilityCommandValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("سبب إخفاء التقييم مطلوب.").When(x => x.Hidden);
        RuleFor(x => x.Reason).MaximumLength(300).When(x => x.Reason is not null);
    }
}

public sealed class AdminSetServiceReviewVisibilityCommandHandler
    : IRequestHandler<AdminSetServiceReviewVisibilityCommand, AdminServiceReviewDto>
{
    private readonly IServiceReviewRepository _reviews;
    private readonly IUserAccountRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;

    public AdminSetServiceReviewVisibilityCommandHandler(
        IServiceReviewRepository reviews, IUserAccountRepository users, IUnitOfWork uow, IAuditLogService audit)
    {
        _reviews = reviews;
        _users = users;
        _uow = uow;
        _audit = audit;
    }

    public async Task<AdminServiceReviewDto> Handle(
        AdminSetServiceReviewVisibilityCommand request, CancellationToken ct)
    {
        var review = await _reviews.GetByIdAsync(request.ReviewId, ct)
            ?? throw new NotFoundException("التقييم غير موجود.");

        var wasHidden = review.IsHidden;
        var now = DateTime.UtcNow;

        if (request.Hidden)
            review.Hide(request.Reason!, now);
        else
            review.Unhide(now);

        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(
            request.AdminUserId,
            request.Hidden ? AuditActions.ServiceReviewHiddenByAdmin : AuditActions.ServiceReviewUnhiddenByAdmin,
            request.IpAddress,
            JsonSerializer.Serialize(new { reviewId = review.Id, hidden = wasHidden }, JsonSerializerOptions.Web),
            JsonSerializer.Serialize(new { reviewId = review.Id, hidden = review.IsHidden }, JsonSerializerOptions.Web),
            ct);

        var reviewer = await _users.GetByIdAsync(review.ReviewerId, ct);
        var name = reviewer is null
            ? "—"
            : string.IsNullOrWhiteSpace(reviewer.DisplayName)
                ? $"{reviewer.FirstName} {reviewer.LastName}".Trim()
                : reviewer.DisplayName!;

        return new AdminServiceReviewDto(
            review.Id, review.ServiceRequestId, name, review.Rating, review.Comment,
            review.IsHidden, review.HiddenReason, review.CreatedAt);
    }
}
