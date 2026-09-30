using MediatR;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.ListDeadLetters;

public sealed record ListDeadLettersQuery(bool? Resolved, int Page, int PageSize) : IRequest<PagedResult<SocialPublicationDeadLetterDto>>;
