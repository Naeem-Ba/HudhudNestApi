using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Commands.DispatchPropertyDistribution;

public sealed class DispatchPropertyDistributionCommandHandler : IRequestHandler<DispatchPropertyDistributionCommand, DistributionRunDto>
{
    private readonly IDistributionEngine _engine;

    public DispatchPropertyDistributionCommandHandler(IDistributionEngine engine) => _engine = engine;

    public Task<DistributionRunDto> Handle(DispatchPropertyDistributionCommand request, CancellationToken ct) =>
        _engine.RunAsync(request.PropertyId, DistributionRunTriggerType.Manual, request.TriggeredByUserId, ct);
}
