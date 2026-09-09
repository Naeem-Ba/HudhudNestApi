using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Queries.GetSocialAccountById;

public sealed class GetSocialAccountByIdQueryHandler : IRequestHandler<GetSocialAccountByIdQuery, SocialAccountDto>
{
    private readonly ISocialAccountRepository _accounts;

    public GetSocialAccountByIdQueryHandler(ISocialAccountRepository accounts) => _accounts = accounts;

    public async Task<SocialAccountDto> Handle(GetSocialAccountByIdQuery request, CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(request.AccountId, ct)
            ?? throw new NotFoundException("الحساب الاجتماعي غير موجود.");

        return SocialDistributionMapper.ToDto(account);
    }
}
