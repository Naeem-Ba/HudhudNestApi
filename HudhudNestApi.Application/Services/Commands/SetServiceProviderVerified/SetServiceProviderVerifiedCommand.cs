using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Commands.SetServiceProviderVerified;

/// <summary>Admin-only — see ServiceProvidersController. Sets the graded verification level;
/// never a bare boolean, see ServiceProviderVerificationLevel's remarks.</summary>
public sealed record SetServiceProviderVerifiedCommand(
    Guid ServiceProviderId,
    ServiceProviderVerificationLevel Level) : IRequest<ServiceProviderDto>;

public sealed class SetServiceProviderVerifiedCommandHandler
    : IRequestHandler<SetServiceProviderVerifiedCommand, ServiceProviderDto>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceReviewRepository _reviews;
    private readonly IUnitOfWork _uow;

    public SetServiceProviderVerifiedCommandHandler(
        IServiceProviderRepository providers, IServiceReviewRepository reviews, IUnitOfWork uow)
    {
        _providers = providers;
        _reviews = reviews;
        _uow = uow;
    }

    public async Task<ServiceProviderDto> Handle(SetServiceProviderVerifiedCommand request, CancellationToken ct)
    {
        var provider = await _providers.GetByIdAsync(request.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        provider.SetVerificationLevel(request.Level, DateTime.UtcNow);
        await _uow.SaveChangesAsync(ct);

        var (average, count) = await _reviews.GetRatingSummaryAsync(provider.Id, ct);
        return ServiceMapper.ToDto(provider, average, count);
    }
}
