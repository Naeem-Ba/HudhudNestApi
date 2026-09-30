using MediatR;
using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Queries.GetServiceCategories;

/// <summary>Static catalog, not DB-backed — the enum itself is the source of truth. Returns
/// the enum member name as Id so the client can look up its own translation key
/// (e.g. "SERVICES.CATEGORY.Verification"); no Arabic/English strings are hardcoded server-side.</summary>
public sealed record GetServiceCategoriesQuery : IRequest<IReadOnlyList<LookupItemDto>>;

public sealed class GetServiceCategoriesQueryHandler
    : IRequestHandler<GetServiceCategoriesQuery, IReadOnlyList<LookupItemDto>>
{
    public Task<IReadOnlyList<LookupItemDto>> Handle(GetServiceCategoriesQuery request, CancellationToken ct)
    {
        var categories = Enum.GetValues<ServiceCategory>()
            .Select(c => new LookupItemDto(((int)c).ToString(), c.ToString()))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<LookupItemDto>>(categories);
    }
}
