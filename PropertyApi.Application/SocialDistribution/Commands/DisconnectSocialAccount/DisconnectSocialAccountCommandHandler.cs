using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Commands.DisconnectSocialAccount;

public sealed class DisconnectSocialAccountCommandHandler : IRequestHandler<DisconnectSocialAccountCommand, SocialAccountDto>
{
    private readonly ISocialAccountRepository _accounts;
    private readonly IUnitOfWork _uow;

    public DisconnectSocialAccountCommandHandler(ISocialAccountRepository accounts, IUnitOfWork uow)
    {
        _accounts = accounts;
        _uow = uow;
    }

    public async Task<SocialAccountDto> Handle(DisconnectSocialAccountCommand request, CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(request.AccountId, ct)
            ?? throw new NotFoundException("الحساب الاجتماعي غير موجود.");

        account.Disconnect();
        _accounts.Update(account);
        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(account);
    }
}
