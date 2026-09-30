using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetDeadLetterById;

public sealed record GetDeadLetterByIdQuery(Guid DeadLetterId) : IRequest<SocialPublicationDeadLetterDto>;
