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

namespace HudhudNestApi.Application.Services.Commands.CompleteServiceRequest;

public sealed record CompleteServiceRequestCommand(
    Guid ServiceRequestId,
    Guid ActorUserId,
    decimal? FinalPrice,
    int? FinalPriceCurrencyId) : IRequest<ServiceRequestDto>;

public sealed class CompleteServiceRequestCommandValidator : AbstractValidator<CompleteServiceRequestCommand>
{
    public CompleteServiceRequestCommandValidator()
    {
        RuleFor(x => x.FinalPrice)
            .GreaterThanOrEqualTo(0).WithMessage("السعر النهائي لا يمكن أن يكون سالباً.")
            .When(x => x.FinalPrice is not null);

        RuleFor(x => x.FinalPriceCurrencyId)
            .NotNull().WithMessage("عملة السعر النهائي مطلوبة عند تحديد سعر.")
            .When(x => x.FinalPrice is not null);
    }
}

public sealed class CompleteServiceRequestCommandHandler
    : IRequestHandler<CompleteServiceRequestCommand, ServiceRequestDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<CompleteServiceRequestCommandHandler> _logger;

    public CompleteServiceRequestCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestStatusHistoryRepository history,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<CompleteServiceRequestCommandHandler> logger)
    {
        _requests = requests;
        _providers = providers;
        _history = history;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ServiceRequestDto> Handle(CompleteServiceRequestCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        if (provider.UserId != request.ActorUserId)
            throw new ForbiddenException("لا يمكنك إتمام طلب خدمة موجّه لمزوّد آخر.");

        var fromStatus = serviceRequest.Status;
        var now = DateTime.UtcNow;

        serviceRequest.Complete(request.FinalPrice, request.FinalPriceCurrencyId, now);

        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                serviceRequest.Id, fromStatus, serviceRequest.Status, request.ActorUserId,
                "تم إتمام الخدمة.", now),
            ct);

        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: serviceRequest.RequesterId,
                propertyId: serviceRequest.PropertyId,
                propertyTitle: serviceRequest.Property?.Title ?? "-",
                type: NotificationType.ServiceRequestCompleted,
                detail: $"تم إتمام طلبك ({serviceRequest.RequestNumber}). يمكنك الآن تقييم الخدمة.",
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send service-request-completed notification. ServiceRequestId={ServiceRequestId}",
                serviceRequest.Id);
        }

        var reloaded = await _requests.GetByIdAsync(serviceRequest.Id, ct) ?? serviceRequest;
        return ServiceMapper.ToDto(reloaded);
    }
}
