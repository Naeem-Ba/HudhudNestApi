using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Services;

namespace HudhudNestApi.Application.SocialDistribution.Queries.PreviewPropertyDistribution;

public sealed class PreviewPropertyDistributionQueryHandler : IRequestHandler<PreviewPropertyDistributionQuery, DistributionPreviewDto>
{
    private readonly IDistributionEngine _engine;

    public PreviewPropertyDistributionQueryHandler(IDistributionEngine engine) => _engine = engine;

    public Task<DistributionPreviewDto> Handle(PreviewPropertyDistributionQuery request, CancellationToken ct) =>
        _engine.PreviewAsync(request.PropertyId, ct);
}
