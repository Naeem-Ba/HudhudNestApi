using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Application.Services.Commands.CancelServiceRequest;

/// <summary>Either the requester or the owning provider may cancel — IsAdmin lets an admin
/// override on behalf of either side, mirroring ReviewsController's admin-delete flag.</summary>
public sealed record CancelServiceRequestCommand(
    Guid ServiceRequestId,
    Guid ActorUserId,
    bool IsAdmin,
    string? Reason) : IRequest<ServiceRequestDto>;

public sealed class CancelServiceRequestCommandValidator : AbstractValidator<CancelServiceRequestCommand>
{
    public CancelServiceRequestCommandValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .When(x => x.Reason is not null);
    }
}

public sealed class CancelServiceRequestCommandHandler
    : IRequestHandler<CancelServiceRequestCommand, ServiceRequestDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<CancelServiceRequestCommandHandler> _logger;

    public CancelServiceRequestCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestStatusHistoryRepository history,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<CancelServiceRequestCommandHandler> logger)
    {
        _requests = requests;
        _providers = providers;
        _history = history;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ServiceRequestDto> Handle(CancelServiceRequestCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        var isRequester = serviceRequest.RequesterId == request.ActorUserId;
        var isProvider = provider.UserId == request.ActorUserId;

        if (!request.IsAdmin && !isRequester && !isProvider)
            throw new ForbiddenException("لا يمكنك إلغاء طلب خدمة لا يخصّك.");

        // Whichever side did NOT cancel is the one that gets notified.
        var recipientId = isRequester ? provider.UserId : serviceRequest.RequesterId;

        var fromStatus = serviceRequest.Status;
        var now = DateTime.UtcNow;

        serviceRequest.Cancel(request.Reason, now);

        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                serviceRequest.Id, fromStatus, serviceRequest.Status, request.ActorUserId,
                request.Reason, now),
            ct);

        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: recipientId,
                propertyId: serviceRequest.PropertyId,
                propertyTitle: serviceRequest.Property?.Title ?? "-",
                type: NotificationType.ServiceRequestCancelled,
                detail: $"تم إلغاء طلب الخدمة ({serviceRequest.RequestNumber}).",
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send service-request-cancelled notification. ServiceRequestId={ServiceRequestId}",
                serviceRequest.Id);
        }

        var reloaded = await _requests.GetByIdAsync(serviceRequest.Id, ct) ?? serviceRequest;
        return ServiceMapper.ToDto(reloaded);
    }
}
