using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.Commands.CreateSocialAccount;

public sealed record CreateSocialAccountCommand(
    Guid SocialChannelId,
    string DisplayName,
    string ExternalAccountId,
    SocialAccountType AccountType,
    int? GovernorateId) : IRequest<SocialAccountDto>;
