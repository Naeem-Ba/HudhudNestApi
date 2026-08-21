using MediatR;
using PropertyApi.Application.Search.DTOs;

namespace PropertyApi.Application.Search.Queries.GetMySavedSearches;

public sealed record GetMySavedSearchesQuery(Guid UserId) : IRequest<IReadOnlyList<SavedSearchDto>>;
