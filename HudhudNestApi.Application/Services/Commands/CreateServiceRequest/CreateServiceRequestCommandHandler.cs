using HudhudNestApi.Application.Notifications.Localization;
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
using HudhudNestApi.Domain.Services.Enums;

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
    private readonly IServiceRequestMessageRepository _messages;
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
        IServiceRequestMessageRepository messages,
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
        _messages = messages;
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

        if (!provider.IsApprovedFor(offering.Category))
            throw new ConflictException("هذه الخدمة غير متاحة حالياً.");

        if (!provider.Covers(property.GovernorateId))
            throw new ConflictException("مزوّد هذه الخدمة لا يغطي محافظة هذا العقار.");

        var hasActive = await _requests.HasActiveRequestAsync(
            request.PropertyId, request.RequesterId, request.ServiceOfferingId, ct);

        if (hasActive)
            throw new ConflictException("يوجد بالفعل طلب قائم لهذه الخدمة على هذا العقار.");

        var requestNumber = await _numbers.NextAsync(ct);
        var now = DateTime.UtcNow;
        // A null intent is an app version from before the conversation stage: confirm at once.
        var legacy = request.Intent is null;

        var serviceRequest = ServiceRequest.Create(
            requestNumber,
            request.PropertyId,
            request.RequesterId,
            provider.Id,
            offering.Id,
            offering.Category,
            request.RequesterNote,
            now,
            request.Intent ?? ServiceRequestIntent.Request);

        await _requests.AddAsync(serviceRequest, ct);

        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                serviceRequest.Id,
                fromStatus: null,
                toStatus: serviceRequest.Status,
                changedByUserId: request.RequesterId,
                note: "فتح العميل محادثة مع المزوّد حول الخدمة.",
                utcNow: now),
            ct);

        if (!string.IsNullOrWhiteSpace(request.RequesterNote))
        {
            await _messages.AddAsync(
                ServiceRequestMessage.Create(
                    serviceRequest.Id, request.RequesterId, ServiceParty.Requester, request.RequesterNote, now),
                ct);
        }

        if (legacy)
        {
            serviceRequest.Confirm(now);
            await _history.AddAsync(
                ServiceRequestStatusHistory.Record(
                    serviceRequest.Id,
                    ServiceRequestStatus.Inquiry,
                    serviceRequest.Status,
                    request.RequesterId,
                    "تم تقديم الطلب واستلامه للمراجعة.",
                    now),
                ct);
        }

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
                type: legacy ? NotificationType.ServiceRequestSubmitted : NotificationType.ServiceInquiryReceived,
                detail: NotificationText.Of(
                    legacy ? NotificationKeys.ServiceSubmittedDetail : NotificationKeys.ServiceInquiryDetail,
                    ("number", requestNumber), ("title", property.Title)),
                relatedEntityId: serviceRequest.Id,
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
        var stats = await _messages.GetStatsAsync([reloaded.Id], ServiceParty.Requester, ct);
        return ServiceMapper.ToDto(
            reloaded, ServiceRequestAudience.Requester,
            stats.GetValueOrDefault(reloaded.Id, ServiceRequestMessageStats.Empty));
    }
}
