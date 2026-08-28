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

namespace PropertyApi.Application.Services.Commands.RejectServiceRequest;

public sealed record RejectServiceRequestCommand(
    Guid ServiceRequestId,
    Guid ActorUserId,
    string Reason) : IRequest<ServiceRequestDto>;

public sealed class RejectServiceRequestCommandValidator : AbstractValidator<RejectServiceRequestCommand>
{
    public RejectServiceRequestCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("سبب رفض الطلب مطلوب.")
            .MaximumLength(500);
    }
}

public sealed class RejectServiceRequestCommandHandler
    : IRequestHandler<RejectServiceRequestCommand, ServiceRequestDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<RejectServiceRequestCommandHandler> _logger;

    public RejectServiceRequestCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestStatusHistoryRepository history,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<RejectServiceRequestCommandHandler> logger)
    {
        _requests = requests;
        _providers = providers;
        _history = history;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ServiceRequestDto> Handle(RejectServiceRequestCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        if (provider.UserId != request.ActorUserId)
            throw new ForbiddenException("لا يمكنك رفض طلب خدمة موجّه لمزوّد آخر.");

        var fromStatus = serviceRequest.Status;
        var now = DateTime.UtcNow;

        serviceRequest.Reject(request.Reason, now);

        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                serviceRequest.Id, fromStatus, serviceRequest.Status, request.ActorUserId,
                request.Reason, now),
            ct);

        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: serviceRequest.RequesterId,
                propertyId: serviceRequest.PropertyId,
                propertyTitle: serviceRequest.Property?.Title ?? "-",
                type: NotificationType.ServiceRequestRejected,
                detail: $"تم رفض طلبك ({serviceRequest.RequestNumber}): {request.Reason}",
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send service-request-rejected notification. ServiceRequestId={ServiceRequestId}",
                serviceRequest.Id);
        }

        var reloaded = await _requests.GetByIdAsync(serviceRequest.Id, ct) ?? serviceRequest;
        return ServiceMapper.ToDto(reloaded);
    }
}
