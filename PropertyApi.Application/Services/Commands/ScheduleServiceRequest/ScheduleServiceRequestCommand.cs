using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Application.Services.Commands.ScheduleServiceRequest;

public sealed record ScheduleServiceRequestCommand(
    Guid ServiceRequestId,
    Guid ActorUserId,
    DateTime ScheduledAt) : IRequest<ServiceRequestDto>;

public sealed class ScheduleServiceRequestCommandValidator : AbstractValidator<ScheduleServiceRequestCommand>
{
    public ScheduleServiceRequestCommandValidator()
    {
        RuleFor(x => x.ScheduledAt)
            .GreaterThan(DateTime.UtcNow)
            .WithMessage("لا يمكن جدولة الخدمة في وقت ماضٍ.");
    }
}

public sealed class ScheduleServiceRequestCommandHandler
    : IRequestHandler<ScheduleServiceRequestCommand, ServiceRequestDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<ScheduleServiceRequestCommandHandler> _logger;

    public ScheduleServiceRequestCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestStatusHistoryRepository history,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<ScheduleServiceRequestCommandHandler> logger)
    {
        _requests = requests;
        _providers = providers;
        _history = history;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ServiceRequestDto> Handle(ScheduleServiceRequestCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        if (provider.UserId != request.ActorUserId)
            throw new ForbiddenException("لا يمكنك جدولة طلب خدمة موجّه لمزوّد آخر.");

        var fromStatus = serviceRequest.Status;
        var now = DateTime.UtcNow;

        serviceRequest.Schedule(request.ScheduledAt, now);

        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                serviceRequest.Id, fromStatus, serviceRequest.Status, request.ActorUserId,
                $"تمت الجدولة بتاريخ {request.ScheduledAt:yyyy-MM-dd HH:mm}.", now),
            ct);

        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: serviceRequest.RequesterId,
                propertyId: serviceRequest.PropertyId,
                propertyTitle: serviceRequest.Property?.Title ?? "-",
                type: NotificationType.ServiceRequestScheduled,
                detail: $"تمت جدولة طلبك ({serviceRequest.RequestNumber}) بتاريخ {request.ScheduledAt:yyyy-MM-dd HH:mm}.",
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send service-request-scheduled notification. ServiceRequestId={ServiceRequestId}",
                serviceRequest.Id);
        }

        var reloaded = await _requests.GetByIdAsync(serviceRequest.Id, ct) ?? serviceRequest;
        return ServiceMapper.ToDto(reloaded);
    }
}
