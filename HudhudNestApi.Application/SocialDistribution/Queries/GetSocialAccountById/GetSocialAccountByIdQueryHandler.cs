using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetSocialAccountById;

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
