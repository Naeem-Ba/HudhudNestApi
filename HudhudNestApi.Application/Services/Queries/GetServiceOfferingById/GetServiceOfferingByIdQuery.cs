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

    public GetServiceOfferingByIdQueryHandler(IServiceOfferingRepository offerings)
        => _offerings = offerings;

    public async Task<ServiceOfferingDto> Handle(GetServiceOfferingByIdQuery request, CancellationToken ct)
    {
        var offering = await _offerings.GetByIdAsync(request.ServiceOfferingId, ct)
            ?? throw new NotFoundException("الخدمة غير موجودة.");

        return ServiceMapper.ToDto(offering);
    }
}
