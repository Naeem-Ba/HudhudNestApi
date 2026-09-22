using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetSocialPublicationStatusHistory;

public sealed record GetSocialPublicationStatusHistoryQuery(Guid PublicationId) : IRequest<IReadOnlyList<SocialPublicationStatusHistoryDto>>;
