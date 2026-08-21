using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Bookings.DTOs;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Bookings.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.Bookings.Commands.RequestVisit;

public sealed class RequestVisitCommandHandler : IRequestHandler<RequestVisitCommand, VisitDto>
{
    private readonly IVisitRepository _visits;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly IPropertyReadRepository _properties;
    private readonly ILogger<RequestVisitCommandHandler> _logger;

    public RequestVisitCommandHandler(
        IVisitRepository visits,
        IUnitOfWork uow,
        INotificationService notifications,
        IPropertyReadRepository properties,
        ILogger<RequestVisitCommandHandler> logger)
    {
        _visits = visits;
        _uow = uow;
        _notifications = notifications;
        _properties = properties;
        _logger = logger;
    }

    public async Task<VisitDto> Handle(RequestVisitCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct)
            ?? throw new NotFoundException($"Property {request.PropertyId} was not found.");

        if (!property.IsPublished)
        {
            throw new DomainException("Cannot request a visit for an unpublished property.");
        }

        if (property.OwnerId == request.RequesterId)
        {
            throw new DomainException("The property owner cannot request a visit for their own property.");
        }

        var hasPending = await _visits.HasPendingVisitAsync(
            request.PropertyId,
            request.RequesterId,
            ct);

        if (hasPending)
        {
            throw new DomainException("There is already a pending visit request for this property.");
        }

        var visit = VisitRequest.Create(
            request.PropertyId,
            request.RequesterId,
            request.ProposedAt,
            request.VisitorName,
            request.VisitorPhone,
            request.VisitorNote);

        await _visits.AddAsync(visit, ct);
        await _uow.SaveChangesAsync(ct);

        /*
         * ✅ إصلاح: توصيل الإشعار للمالك ليس حرجًا — طلب الزيارة نفسه
         * تم حفظه بنجاح في السطر أعلاه بالفعل. سابقًا كان هذا الاستدعاء
         * بدون try/catch (خلافًا لنفس النمط الموجود في
         * SendMessageCommandHandler)، فإذا فشل SignalR أو أي جزء من
         * الإشعار لأي سبب، كان الاستثناء يتسبب بإفشال طلب
         * POST /api/Visits بأكمله (٥٠٠) رغم أن الزيارة محفوظة فعليًا —
         * وهذا بالضبط ما يفسّر شكوى "طلب الزيارة لم يصل ابدًا": الزائر
         * كان يرى رسالة خطأ رغم أن الطلب وصل وحُفظ في قاعدة البيانات.
         */
        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: property.OwnerId,
                propertyId: property.Id,
                propertyTitle: property.Title,
                type: NotificationType.VisitRequested,
                detail: $"{request.VisitorName} requested a visit scheduled for {request.ProposedAt:dd/MM/yyyy HH:mm}.",
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send visit-request notification. " +
                "VisitId={VisitId}, PropertyId={PropertyId}, OwnerId={OwnerId}, RequesterId={RequesterId}",
                visit.Id,
                property.Id,
                property.OwnerId,
                request.RequesterId);
        }

        return new VisitDto(
            Id: visit.Id,
            PropertyId: visit.PropertyId,
            PropertyTitle: property.Title,
            PropertyCity: property.City,
            PropertyMainImageUrl: property.MainImageUrl,
            RequesterId: request.RequesterId,
            RequesterName: request.VisitorName,
            VisitorName: visit.VisitorName,
            VisitorPhone: visit.VisitorPhone,
            VisitorNote: visit.VisitorNote,
            ProposedAt: visit.ProposedAt,
            OwnerNote: visit.OwnerNote,
            RespondedAt: visit.RespondedAt,
            Status: visit.Status,
            CreatedAt: visit.CreatedAt,
            // ✅ إصلاح: كان غائبًا — راجع تعليق VisitDto.cs
            OwnerId: property.OwnerId);
    }
}

