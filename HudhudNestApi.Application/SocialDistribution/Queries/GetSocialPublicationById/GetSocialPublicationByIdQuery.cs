using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetSocialPublicationById;

public sealed record GetSocialPublicationByIdQuery(Guid PublicationId) : IRequest<SocialPublicationDto>;
