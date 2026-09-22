using MediatR;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;

namespace HudhudNestApi.Application.Services.Queries.GetMyServiceProvider;

/// <summary>Null result (not NotFoundException) — "the current user is not a provider" is a
/// normal, expected state, not an error, exactly like GetMyAgencyQuery.</summary>
public sealed record GetMyServiceProviderQuery(Guid UserId) : IRequest<ServiceProviderDto?>;

public sealed class GetMyServiceProviderQueryHandler
    : IRequestHandler<GetMyServiceProviderQuery, ServiceProviderDto?>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceReviewRepository _reviews;

    public GetMyServiceProviderQueryHandler(
        IServiceProviderRepository providers, IServiceReviewRepository reviews)
    {
        _providers = providers;
        _reviews = reviews;
    }

    public async Task<ServiceProviderDto?> Handle(GetMyServiceProviderQuery request, CancellationToken ct)
    {
        var provider = await _providers.GetByUserIdAsync(request.UserId, ct);
        if (provider is null)
            return null;

        var (average, count) = await _reviews.GetRatingSummaryAsync(provider.Id, ct);
        return ServiceMapper.ToDto(provider, average, count);
    }
}
