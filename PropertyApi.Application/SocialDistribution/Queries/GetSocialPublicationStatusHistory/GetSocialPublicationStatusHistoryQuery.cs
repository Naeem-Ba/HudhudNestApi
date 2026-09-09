using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.GetSocialPublicationStatusHistory;

public sealed record GetSocialPublicationStatusHistoryQuery(Guid PublicationId) : IRequest<IReadOnlyList<SocialPublicationStatusHistoryDto>>;
