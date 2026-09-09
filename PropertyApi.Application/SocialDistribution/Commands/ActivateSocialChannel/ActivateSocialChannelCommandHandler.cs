using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Commands.ActivateSocialChannel;

public sealed class ActivateSocialChannelCommandHandler : IRequestHandler<ActivateSocialChannelCommand, SocialChannelDto>
{
    private readonly ISocialChannelRepository _channels;
    private readonly IUnitOfWork _uow;

    public ActivateSocialChannelCommandHandler(ISocialChannelRepository channels, IUnitOfWork uow)
    {
        _channels = channels;
        _uow = uow;
    }

    public async Task<SocialChannelDto> Handle(ActivateSocialChannelCommand request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException("القناة الاجتماعية غير موجودة.");

        channel.Activate();
        _channels.Update(channel);
        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(channel);
    }
}
