using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Commands.CreateSocialAccount;

public sealed record CreateSocialAccountCommand(
    Guid SocialChannelId,
    string DisplayName,
    string ExternalAccountId,
    SocialAccountType AccountType,
    int? GovernorateId) : IRequest<SocialAccountDto>;
