using MediatR;
using HudhudNestApi.Application.Search.DTOs;

namespace HudhudNestApi.Application.Search.Queries.GetMySavedSearches;

public sealed record GetMySavedSearchesQuery(Guid UserId) : IRequest<IReadOnlyList<SavedSearchDto>>;
