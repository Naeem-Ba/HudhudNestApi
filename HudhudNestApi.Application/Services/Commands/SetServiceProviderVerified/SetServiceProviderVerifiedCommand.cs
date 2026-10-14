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
    ServiceProviderVerificationLevel Level,
    Guid? AdminUserId = null,
    string? IpAddress = null) : IRequest<ServiceProviderDto>;

public sealed class SetServiceProviderVerifiedCommandHandler
    : IRequestHandler<SetServiceProviderVerifiedCommand, ServiceProviderDto>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceReviewRepository _reviews;
    private readonly IUnitOfWork _uow;
    private readonly IServiceProviderEventRepository _events;
    private readonly IAuditLogService _audit;

    public SetServiceProviderVerifiedCommandHandler(
        IServiceProviderRepository providers,
        IServiceReviewRepository reviews,
        IUnitOfWork uow,
        IServiceProviderEventRepository events,
        IAuditLogService audit)
    {
        _providers = providers;
        _reviews = reviews;
        _uow = uow;
        _events = events;
        _audit = audit;
    }

    public async Task<ServiceProviderDto> Handle(SetServiceProviderVerifiedCommand request, CancellationToken ct)
    {
        var provider = await _providers.GetByIdAsync(request.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        var now = DateTime.UtcNow;
        var before = provider.VerificationLevel;
        provider.SetVerificationLevel(request.Level, now);
        await _events.AddAsync(Domain.Services.Entities.ServiceProviderEvent.Record(
            null, provider.Id, request.AdminUserId, ServiceProviderEventType.VerificationChanged,
            $"{before} -> {request.Level}", isInternal: true, now), ct);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(
            request.AdminUserId, Domain.Audit.Constants.AuditActions.ServiceProviderVerificationChangedByAdmin,
            request.IpAddress,
            System.Text.Json.JsonSerializer.Serialize(new { providerId = provider.Id, level = before.ToString() }),
            System.Text.Json.JsonSerializer.Serialize(new { providerId = provider.Id, level = request.Level.ToString() }),
            ct);

        var (average, count) = await _reviews.GetRatingSummaryAsync(provider.Id, ct);
        return ServiceMapper.ToDto(provider, average, count);
    }
}
