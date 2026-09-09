using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Services;

namespace PropertyApi.Application.SocialDistribution.Queries.PreviewPropertyDistribution;

public sealed class PreviewPropertyDistributionQueryHandler : IRequestHandler<PreviewPropertyDistributionQuery, DistributionPreviewDto>
{
    private readonly IDistributionEngine _engine;

    public PreviewPropertyDistributionQueryHandler(IDistributionEngine engine) => _engine = engine;

    public Task<DistributionPreviewDto> Handle(PreviewPropertyDistributionQuery request, CancellationToken ct) =>
        _engine.PreviewAsync(request.PropertyId, ct);
}
