using MediatR;
using PropertyApi.Application.Bookings.DTOs;
using PropertyApi.Application.Bookings.Interfaces;

namespace PropertyApi.Application.Bookings.Queries.GetMyVisits;

public sealed class GetMyVisitsQueryHandler
    : IRequestHandler<GetMyVisitsQuery, IReadOnlyList<VisitDto>>
{
    private readonly IVisitRepository _visits;

    public GetMyVisitsQueryHandler(IVisitRepository visits)
        => _visits = visits;

    public async Task<IReadOnlyList<VisitDto>> Handle(
        GetMyVisitsQuery request, CancellationToken ct)
    {
        // ✅ إصلاح: كانت GetByRequesterIdAsync — راجع تعليق IVisitRepository.
        // "زياراتي" الآن تعني: ما طلبتُه كزائر + ما وصلني كمالك عقار.
        var visits = await _visits.GetByRequesterOrOwnerIdAsync(request.UserId, ct);

        return visits
            .Where(v => request.StatusFilter is null || v.Status == request.StatusFilter)
            .OrderByDescending(v => v.ProposedAt)
            .Select(v => new VisitDto(
                Id: v.Id,
                PropertyId: v.PropertyId,
                PropertyTitle: v.Property?.Title ?? "-",
                PropertyCity: v.Property?.City ?? "-",
                PropertyMainImageUrl: v.Property?.Images
                    .FirstOrDefault(i => i.IsMain)?.Url,
                RequesterId: v.RequesterId,
                RequesterName: $"{v.Requester?.FirstName} {v.Requester?.LastName}".Trim(),
                VisitorName: v.VisitorName,
                VisitorPhone: v.VisitorPhone,
                VisitorNote: v.VisitorNote,
                ProposedAt: v.ProposedAt,
                OwnerNote: v.OwnerNote,
                RespondedAt: v.RespondedAt,
                Status: v.Status,
                CreatedAt: v.CreatedAt,
                // ✅ إصلاح: كان غائبًا — راجع تعليق VisitDto.cs
                OwnerId: v.Property?.OwnerId ?? Guid.Empty))
            .ToList()
            .AsReadOnly();
    }
}
