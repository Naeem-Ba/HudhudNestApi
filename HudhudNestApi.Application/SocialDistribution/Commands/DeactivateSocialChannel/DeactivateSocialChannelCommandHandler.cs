using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Commands.DeactivateSocialChannel;

public sealed class DeactivateSocialChannelCommandHandler : IRequestHandler<DeactivateSocialChannelCommand, SocialChannelDto>
{
    private readonly ISocialChannelRepository _channels;
    private readonly IUnitOfWork _uow;

    public DeactivateSocialChannelCommandHandler(ISocialChannelRepository channels, IUnitOfWork uow)
    {
        _channels = channels;
        _uow = uow;
    }

    public async Task<SocialChannelDto> Handle(DeactivateSocialChannelCommand request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException("القناة الاجتماعية غير موجودة.");

        channel.Deactivate();
        _channels.Update(channel);
        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(channel);
    }
}
