using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Commands.ServiceOutcome;

// ─────────────────────────────────────────────────────────────────────────────
// Provider: "the service did not happen"
// ─────────────────────────────────────────────────────────────────────────────

public sealed record MarkServiceRequestNotCompletedCommand(
    Guid ServiceRequestId,
    Guid ActorUserId,
    ServiceOutcomeReason Reason,
    string? Note) : IRequest<ServiceRequestDto>;

public sealed class MarkServiceRequestNotCompletedCommandValidator
    : AbstractValidator<MarkServiceRequestNotCompletedCommand>
{
    public MarkServiceRequestNotCompletedCommandValidator()
    {
        RuleFor(x => x.Reason).IsInEnum().WithMessage("اختر سبب عدم إتمام الخدمة.");
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}

public sealed class MarkServiceRequestNotCompletedCommandHandler
    : IRequestHandler<MarkServiceRequestNotCompletedCommand, ServiceRequestDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<MarkServiceRequestNotCompletedCommandHandler> _logger;

    public MarkServiceRequestNotCompletedCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestStatusHistoryRepository history,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<MarkServiceRequestNotCompletedCommandHandler> logger)
    {
        _requests = requests;
        _providers = providers;
        _history = history;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ServiceRequestDto> Handle(MarkServiceRequestNotCompletedCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        if (provider.UserId != request.ActorUserId)
            throw new ForbiddenException("لا يمكنك إغلاق طلب خدمة موجّه لمزوّد آخر.");

        var fromStatus = serviceRequest.Status;
        var now = DateTime.UtcNow;

        serviceRequest.MarkNotCompleted(request.Reason, request.Note, now);

        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                serviceRequest.Id, fromStatus, serviceRequest.Status, request.ActorUserId,
                $"لم تتم الخدمة: {request.Reason}", now),
            ct);

        await _uow.SaveChangesAsync(ct);

        await NotifyRequesterAsync(serviceRequest, _notifications, _logger, ct);

        var reloaded = await _requests.GetByIdAsync(serviceRequest.Id, ct) ?? serviceRequest;
        return ServiceMapper.ToDto(reloaded);
    }

    internal static async Task NotifyRequesterAsync(
        ServiceRequest serviceRequest, INotificationService notifications, ILogger logger, CancellationToken ct)
    {
        try
        {
            await notifications.NotifyPropertyUpdateAsync(
                recipientId: serviceRequest.RequesterId,
                propertyId: serviceRequest.PropertyId,
                propertyTitle: serviceRequest.Property?.Title ?? "-",
                type: NotificationType.ServiceRequestNotCompleted,
                detail: NotificationText.Of(NotificationKeys.ServiceNotCompletedDetail, ("number", serviceRequest.RequestNumber)),
                ct: ct);
        }
        catch (Exception ex)
        {
            // The outcome is already committed: a failed notification must not undo or fail it.
            logger.LogError(ex,
                "Failed to send service-request-not-completed notification. ServiceRequestId={ServiceRequestId}",
                serviceRequest.Id);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Customer: "is that right?" + their own reason
// ─────────────────────────────────────────────────────────────────────────────

public sealed record SubmitServiceRequestFeedbackCommand(
    Guid ServiceRequestId,
    Guid ActorUserId,
    ServiceRequesterAnswer Answer,
    ServiceRequesterReason? Reason,
    string? Note) : IRequest<ServiceRequestDto>;

public sealed class SubmitServiceRequestFeedbackCommandValidator
    : AbstractValidator<SubmitServiceRequestFeedbackCommand>
{
    public SubmitServiceRequestFeedbackCommandValidator()
    {
        RuleFor(x => x.Answer).IsInEnum().WithMessage("اختر إجابة.");
        RuleFor(x => x.Reason).IsInEnum().When(x => x.Reason is not null).WithMessage("السبب غير معروف.");
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}

public sealed class SubmitServiceRequestFeedbackCommandHandler
    : IRequestHandler<SubmitServiceRequestFeedbackCommand, ServiceRequestDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;

    public SubmitServiceRequestFeedbackCommandHandler(
        IServiceRequestRepository requests,
        IServiceRequestStatusHistoryRepository history,
        IUnitOfWork uow)
    {
        _requests = requests;
        _history = history;
        _uow = uow;
    }

    public async Task<ServiceRequestDto> Handle(SubmitServiceRequestFeedbackCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        if (serviceRequest.RequesterId != request.ActorUserId)
            throw new ForbiddenException("لا يمكنك الرد على طلب خدمة لم تُقدّمه أنت.");

        var now = DateTime.UtcNow;
        serviceRequest.RecordRequesterFeedback(request.Answer, request.Reason, request.Note, now);

        // Same-status history row (the provider can read the history, so the customer's reason
        // stays out of it: it lives on the request, visible to the customer and the admin only).
        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                serviceRequest.Id, serviceRequest.Status, serviceRequest.Status, request.ActorUserId,
                request.Answer == ServiceRequesterAnswer.Disputed
                    ? "اعترض الطالب على نتيجة الطلب."
                    : "أكّد الطالب نتيجة الطلب.",
                now),
            ct);

        await _uow.SaveChangesAsync(ct);

        var reloaded = await _requests.GetByIdAsync(serviceRequest.Id, ct) ?? serviceRequest;
        return ServiceMapper.ToDto(reloaded, ServiceRequestAudience.Requester);
    }
}
