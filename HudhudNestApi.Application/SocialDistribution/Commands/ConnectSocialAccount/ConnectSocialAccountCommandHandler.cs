using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Commands.ConnectSocialAccount;

public sealed class ConnectSocialAccountCommandHandler : IRequestHandler<ConnectSocialAccountCommand, SocialAccountDto>
{
    private readonly ISocialAccountRepository _accounts;
    private readonly IUnitOfWork _uow;

    public ConnectSocialAccountCommandHandler(ISocialAccountRepository accounts, IUnitOfWork uow)
    {
        _accounts = accounts;
        _uow = uow;
    }

    public async Task<SocialAccountDto> Handle(ConnectSocialAccountCommand request, CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(request.AccountId, ct)
            ?? throw new NotFoundException("الحساب الاجتماعي غير موجود.");

        account.Connect(request.CredentialReference);
        _accounts.Update(account);
        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(account);
    }
}
