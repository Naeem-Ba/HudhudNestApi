using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Commands.CreateSocialChannel;

/// <summary>Enforces the one-channel-per-platform design decision (see SocialChannel remarks).</summary>
public sealed class CreateSocialChannelCommandHandler : IRequestHandler<CreateSocialChannelCommand, SocialChannelDto>
{
    private readonly ISocialChannelRepository _channels;
    private readonly IUnitOfWork _uow;

    public CreateSocialChannelCommandHandler(ISocialChannelRepository channels, IUnitOfWork uow)
    {
        _channels = channels;
        _uow = uow;
    }

    public async Task<SocialChannelDto> Handle(CreateSocialChannelCommand request, CancellationToken ct)
    {
        var existing = await _channels.GetByPlatformAsync(request.Platform, ct);
        if (existing is not null)
            throw new ConflictException("توجد قناة مسجلة لهذه المنصة بالفعل.");

        var channel = SocialChannel.Create(request.Platform, request.Name, request.ConfigurationVersion);

        await _channels.AddAsync(channel, ct);
        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(channel);
    }
}
