using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Application.SocialDistribution.Commands.CreateSocialAccount;

public sealed class CreateSocialAccountCommandHandler : IRequestHandler<CreateSocialAccountCommand, SocialAccountDto>
{
    private readonly ISocialChannelRepository _channels;
    private readonly ISocialAccountRepository _accounts;
    private readonly IUnitOfWork _uow;

    public CreateSocialAccountCommandHandler(
        ISocialChannelRepository channels,
        ISocialAccountRepository accounts,
        IUnitOfWork uow)
    {
        _channels = channels;
        _accounts = accounts;
        _uow = uow;
    }

    public async Task<SocialAccountDto> Handle(CreateSocialAccountCommand request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.SocialChannelId, ct)
            ?? throw new NotFoundException("القناة الاجتماعية غير موجودة.");

        if (!channel.CanBackNewAccounts())
            throw new ConflictException("لا يمكن إضافة حساب جديد على قناة غير فعّالة.");

        var duplicate = await _accounts.ExternalAccountExistsAsync(channel.Platform, request.ExternalAccountId, ct);
        if (duplicate)
            throw new ConflictException("هذا الحساب مضاف بالفعل لهذه المنصة.");

        var account = SocialAccount.Create(
            channel.Id,
            channel.Platform,
            request.DisplayName,
            request.ExternalAccountId,
            request.AccountType,
            request.GovernorateId);

        await _accounts.AddAsync(account, ct);
        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(account);
    }
}
