using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;

namespace PropertyApi.Application.Services.Queries.GetServiceOfferingById;

public sealed record GetServiceOfferingByIdQuery(Guid ServiceOfferingId) : IRequest<ServiceOfferingDto>;

public sealed class GetServiceOfferingByIdQueryHandler
    : IRequestHandler<GetServiceOfferingByIdQuery, ServiceOfferingDto>
{
    private readonly IServiceOfferingRepository _offerings;

    public GetServiceOfferingByIdQueryHandler(IServiceOfferingRepository offerings)
        => _offerings = offerings;

    public async Task<ServiceOfferingDto> Handle(GetServiceOfferingByIdQuery request, CancellationToken ct)
    {
        var offering = await _offerings.GetByIdAsync(request.ServiceOfferingId, ct)
            ?? throw new NotFoundException("الخدمة غير موجودة.");

        return ServiceMapper.ToDto(offering);
    }
}
