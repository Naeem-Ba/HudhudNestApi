using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;

namespace HudhudNestApi.Application.Services.Queries.GetServiceOfferingById;

public sealed record GetServiceOfferingByIdQuery(Guid ServiceOfferingId) : IRequest<ServiceOfferingDto>;

public sealed class GetServiceOfferingByIdQueryHandler
    : IRequestHandler<GetServiceOfferingByIdQuery, ServiceOfferingDto>
{
    private readonly IServiceOfferingRepository _offerings;
    private readonly IServiceReviewRepository _reviews;

    public GetServiceOfferingByIdQueryHandler(IServiceOfferingRepository offerings, IServiceReviewRepository reviews)
    {
        _offerings = offerings;
        _reviews = reviews;
    }

    public async Task<ServiceOfferingDto> Handle(GetServiceOfferingByIdQuery request, CancellationToken ct)
    {
        var offering = await _offerings.GetByIdAsync(request.ServiceOfferingId, ct)
            ?? throw new NotFoundException("الخدمة غير موجودة.");

        // The catalog lists only active offerings of Active providers; the same offering must not
        // stay readable by id once it is switched off or its provider is suspended (P11-D08).
        if (!offering.IsActive || offering.ServiceProvider is not { IsActive: true })
            throw new NotFoundException("الخدمة غير موجودة.");

        return (await new[] { offering }.ToDtosWithRatingsAsync(_reviews, ct))[0];
    }
}
