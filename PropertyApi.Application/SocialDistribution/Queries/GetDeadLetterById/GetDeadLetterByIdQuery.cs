using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.GetDeadLetterById;

public sealed record GetDeadLetterByIdQuery(Guid DeadLetterId) : IRequest<SocialPublicationDeadLetterDto>;
