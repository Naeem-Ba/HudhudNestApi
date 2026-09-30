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

namespace HudhudNestApi.Application.Services.Commands.CreateServiceRequest;

/// <summary>
/// Creates a ServiceRequest against one property/offering pair. Unlike RequestVisitCommand,
/// this does NOT block the property's own owner from requesting — verifying/valuing/inspecting
/// one's own listing is a normal, expected use of the marketplace, not a conflict of interest
/// the way "reviewing your own property" would be.
/// </summary>
public sealed class CreateServiceRequestCommandHandler
    : IRequestHandler<CreateServiceRequestCommand, ServiceRequestDto>
{
    private readonly IPropertyReadRepository _properties;
    private readonly IServiceOfferingRepository _offerings;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IServiceRequestNumberGenerator _numbers;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<CreateServiceRequestCommandHandler> _logger;

    public CreateServiceRequestCommandHandler(
        IPropertyReadRepository properties,
        IServiceOfferingRepository offerings,
        IServiceProviderRepository providers,
        IServiceRequestRepository requests,
        IServiceRequestStatusHistoryRepository history,
        IServiceRequestNumberGenerator numbers,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<CreateServiceRequestCommandHandler> logger)
    {
        _properties = properties;
        _offerings = offerings;
        _providers = providers;
        _requests = requests;
        _history = history;
        _numbers = numbers;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ServiceRequestDto> Handle(CreateServiceRequestCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct)
            ?? throw new NotFoundException("العقار غير موجود.");

        var offering = await _offerings.GetByIdAsync(request.ServiceOfferingId, ct)
            ?? throw new NotFoundException("الخدمة غير موجودة.");

        if (!offering.IsActive)
            throw new ConflictException("هذه الخدمة غير متاحة حالياً.");

        var provider = await _providers.GetByIdAsync(offering.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        if (!provider.IsActive)
            throw new ConflictException("مزوّد هذه الخدمة غير نشط حالياً.");

        var hasActive = await _requests.HasActiveRequestAsync(
            request.PropertyId, request.RequesterId, request.ServiceOfferingId, ct);

        if (hasActive)
            throw new ConflictException("يوجد بالفعل طلب قائم لهذه الخدمة على هذا العقار.");

        var requestNumber = await _numbers.NextAsync(ct);
        var now = DateTime.UtcNow;

        var serviceRequest = ServiceRequest.Create(
            requestNumber,
            request.PropertyId,
            request.RequesterId,
            provider.Id,
            offering.Id,
            offering.Category,
            request.RequesterNote,
            now);

        await _requests.AddAsync(serviceRequest, ct);

        await _history.AddAsync(
            Domain.Services.Entities.ServiceRequestStatusHistory.Record(
                serviceRequest.Id,
                fromStatus: null,
                toStatus: serviceRequest.Status,
                changedByUserId: request.RequesterId,
                note: "تم تقديم الطلب واستلامه للمراجعة.",
                utcNow: now),
            ct);

        await _uow.SaveChangesAsync(ct);

        // Best-effort, same pattern as RequestVisitCommandHandler/AddReviewCommandHandler: the
        // request itself is already committed above, so a notification failure must never turn
        // an otherwise-successful submission into a 500.
        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: provider.UserId,
                propertyId: property.Id,
                propertyTitle: property.Title,
                type: NotificationType.ServiceRequestSubmitted,
                detail: $"طلب خدمة جديد ({requestNumber}) على عقار '{property.Title}'.",
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send service-request-submitted notification. ServiceRequestId={ServiceRequestId}, ProviderId={ProviderId}",
                serviceRequest.Id,
                provider.Id);
        }

        var reloaded = await _requests.GetByIdAsync(serviceRequest.Id, ct) ?? serviceRequest;
        return ServiceMapper.ToDto(reloaded);
    }
}
