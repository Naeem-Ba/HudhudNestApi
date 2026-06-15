using MediatR;
using PropertyApi.Application.Bookings.DTOs;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Exceptions;

namespace PropertyApi.Application.Bookings.Queries.GetPropertyVisits;

public sealed class GetPropertyVisitsQueryHandler
    : IRequestHandler<GetPropertyVisitsQuery, IReadOnlyList<VisitDto>>
{
    private readonly IVisitRepository    _visits;
    private readonly IPropertyReadRepository _properties;

    public GetPropertyVisitsQueryHandler(
        IVisitRepository visits, IPropertyReadRepository properties)
    {
        _visits = visits; _properties = properties;
    }

    public async Task<IReadOnlyList<VisitDto>> Handle(
        GetPropertyVisitsQuery request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct)
            ?? throw new NotFoundException("العقار غير موجود.");

        if (property.OwnerId != request.OwnerId)
            throw new ForbiddenException("فقط مالك العقار يمكنه رؤية طلبات الزيارة.");

        var visits = await _visits.GetByPropertyIdAsync(request.PropertyId, ct);

        return visits
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new VisitDto(
                Id:                  v.Id,
                PropertyId:          v.PropertyId,
                PropertyTitle:       property.Title,
                PropertyCity:        property.City,
                PropertyMainImageUrl: null,
                RequesterId:         v.RequesterId,
                RequesterName:       $"{v.Requester?.FirstName} {v.Requester?.LastName}".Trim(),
                VisitorName:         v.VisitorName,
                VisitorPhone:        v.VisitorPhone,
                VisitorNote:         v.VisitorNote,
                ProposedAt:          v.ProposedAt,
                OwnerNote:           v.OwnerNote,
                RespondedAt:         v.RespondedAt,
                Status:              v.Status,
                CreatedAt:           v.CreatedAt))
            .ToList()
            .AsReadOnly();
    }
}