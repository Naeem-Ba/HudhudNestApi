using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Services;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.Commands.DispatchPropertyDistribution;

public sealed class DispatchPropertyDistributionCommandHandler : IRequestHandler<DispatchPropertyDistributionCommand, DistributionRunDto>
{
    private readonly IDistributionEngine _engine;

    public DispatchPropertyDistributionCommandHandler(IDistributionEngine engine) => _engine = engine;

    public Task<DistributionRunDto> Handle(DispatchPropertyDistributionCommand request, CancellationToken ct) =>
        _engine.RunAsync(request.PropertyId, DistributionRunTriggerType.Manual, request.TriggeredByUserId, ct);
}
