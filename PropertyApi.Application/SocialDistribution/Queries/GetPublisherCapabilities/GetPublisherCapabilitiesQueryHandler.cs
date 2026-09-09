using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;

namespace PropertyApi.Application.SocialDistribution.Queries.GetPublisherCapabilities;

public sealed class GetPublisherCapabilitiesQueryHandler : IRequestHandler<GetPublisherCapabilitiesQuery, SocialPublisherInfoDto>
{
    private readonly ISocialPublisherRegistry _registry;

    public GetPublisherCapabilitiesQueryHandler(ISocialPublisherRegistry registry) => _registry = registry;

    public Task<SocialPublisherInfoDto> Handle(GetPublisherCapabilitiesQuery request, CancellationToken ct)
    {
        var publisher = _registry.TryGetPublisher(request.Platform)
            ?? throw new NotFoundException($"لا يوجد Publisher مسجل لمنصة {request.Platform}.");

        return Task.FromResult(new SocialPublisherInfoDto(request.Platform, publisher.GetCapabilities()));
    }
}
