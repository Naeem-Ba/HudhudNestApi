using MediatR;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.ListDeadLetters;

public sealed record ListDeadLettersQuery(bool? Resolved, int Page, int PageSize) : IRequest<PagedResult<SocialPublicationDeadLetterDto>>;
