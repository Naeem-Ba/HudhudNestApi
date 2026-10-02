using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Commands.DispatchPropertyDistribution;

public sealed class DispatchPropertyDistributionCommandHandler : IRequestHandler<DispatchPropertyDistributionCommand, DistributionRunDto>
{
    private readonly IDistributionEngine _engine;
    private readonly ISocialDistributionSwitch? _distributionSwitch;

    public DispatchPropertyDistributionCommandHandler(IDistributionEngine engine, ISocialDistributionSwitch? distributionSwitch = null)
    {
        _engine = engine;
        _distributionSwitch = distributionSwitch;
    }

    public async Task<DistributionRunDto> Handle(DispatchPropertyDistributionCommand request, CancellationToken ct)
    {
        if (_distributionSwitch is { IsEnabled: false })
            throw new ConflictException("التوزيع الاجتماعي موقوف حالياً (SocialDistribution:Enabled=false). أعد تفعيله ثم أعد المحاولة.");

        return await _engine.RunAsync(request.PropertyId, DistributionRunTriggerType.Manual, request.TriggeredByUserId, ct);
    }
}
