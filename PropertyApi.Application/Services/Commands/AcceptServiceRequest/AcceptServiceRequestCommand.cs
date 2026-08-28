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

namespace PropertyApi.Application.Services.Commands.AcceptServiceRequest;

/// <summary>Provider-only. ActorUserId is compared against ServiceProvider.UserId — the
/// provider that owns this request's offering, resolved server-side, never client-supplied.</summary>
public sealed record AcceptServiceRequestCommand(
    Guid ServiceRequestId,
    Guid ActorUserId,
    decimal? QuotedPrice,
    int? QuotedPriceCurrencyId,
    string? ProviderNote) : IRequest<ServiceRequestDto>;

public sealed class AcceptServiceRequestCommandHandler
    : IRequestHandler<AcceptServiceRequestCommand, ServiceRequestDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<AcceptServiceRequestCommandHandler> _logger;

    public AcceptServiceRequestCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestStatusHistoryRepository history,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<AcceptServiceRequestCommandHandler> logger)
    {
        _requests = requests;
        _providers = providers;
        _history = history;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ServiceRequestDto> Handle(AcceptServiceRequestCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        if (provider.UserId != request.ActorUserId)
            throw new ForbiddenException("لا يمكنك قبول طلب خدمة موجّه لمزوّد آخر.");

        var fromStatus = serviceRequest.Status;
        var now = DateTime.UtcNow;

        serviceRequest.Accept(request.QuotedPrice, request.QuotedPriceCurrencyId, request.ProviderNote, now);

        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                serviceRequest.Id, fromStatus, serviceRequest.Status, request.ActorUserId,
                "تم قبول الطلب من قِبل مزوّد الخدمة.", now),
            ct);

        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: serviceRequest.RequesterId,
                propertyId: serviceRequest.PropertyId,
                propertyTitle: serviceRequest.Property?.Title ?? "-",
                type: NotificationType.ServiceRequestAccepted,
                detail: $"تم قبول طلبك ({serviceRequest.RequestNumber}) من قِبل {provider.DisplayName}.",
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send service-request-accepted notification. ServiceRequestId={ServiceRequestId}",
                serviceRequest.Id);
        }

        var reloaded = await _requests.GetByIdAsync(serviceRequest.Id, ct) ?? serviceRequest;
        return ServiceMapper.ToDto(reloaded);
    }
}
