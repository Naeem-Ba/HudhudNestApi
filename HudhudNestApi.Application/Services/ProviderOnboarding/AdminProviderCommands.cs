using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Services;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.ProviderOnboarding;

// Admin-only, see AdminApplicationCommands.cs.

// ── Directory and detail ─────────────────────────────────────────────────

public sealed record SearchServiceProvidersQuery(ServiceProviderFilter Filter)
    : IRequest<ServicePageDto<AdminServiceProviderSummaryDto>>;

public sealed class SearchServiceProvidersQueryHandler
    : IRequestHandler<SearchServiceProvidersQuery, ServicePageDto<AdminServiceProviderSummaryDto>>
{
    private readonly IServiceProviderRepository _providers;

    public SearchServiceProvidersQueryHandler(IServiceProviderRepository providers) => _providers = providers;

    public async Task<ServicePageDto<AdminServiceProviderSummaryDto>> Handle(
        SearchServiceProvidersQuery request, CancellationToken ct)
    {
        var filter = ProviderAdminPaging.Normalize(request.Filter);
        var (items, total) = await _providers.SearchAsync(filter, ct);
        return new(items.Select(ProviderOnboardingMapper.ToSummary).ToList(), total, filter.Page, filter.PageSize);
    }
}

public sealed record GetServiceProviderForAdminQuery(Guid ServiceProviderId) : IRequest<AdminServiceProviderDetailDto>;

public sealed class GetServiceProviderForAdminQueryHandler
    : IRequestHandler<GetServiceProviderForAdminQuery, AdminServiceProviderDetailDto>
{
    private readonly ProviderAdminDetailBuilder _builder;

    public GetServiceProviderForAdminQueryHandler(ProviderAdminDetailBuilder builder) => _builder = builder;

    public async Task<AdminServiceProviderDetailDto> Handle(GetServiceProviderForAdminQuery request, CancellationToken ct)
        => await _builder.BuildAsync(request.ServiceProviderId, ct);
}

// ── Update profile and approved scope ────────────────────────────────────

public sealed record UpdateServiceProviderByAdminCommand(
    Guid AdminUserId,
    Guid ServiceProviderId,
    string DisplayName,
    string? Bio,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<ServiceCategory> ApprovedCategories,
    IReadOnlyList<int> CoverageGovernorateIds,
    string? IpAddress) : IRequest<AdminServiceProviderDetailDto>;

public sealed class UpdateServiceProviderByAdminCommandValidator
    : AbstractValidator<UpdateServiceProviderByAdminCommand>
{
    public UpdateServiceProviderByAdminCommandValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Bio).MaximumLength(2000);
        RuleFor(x => x.ContactEmail)
            .EmailAddress().WithMessage("صيغة البريد الإلكتروني غير صحيحة.")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.ContactPhone)
            .Matches(@"^\+?[0-9\s\-]{7,20}$").WithMessage("صيغة رقم الهاتف غير صحيحة.")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactPhone));
        RuleFor(x => x.ApprovedCategories).NotEmpty().WithMessage("اختر نوع خدمة معتمدًا واحدًا على الأقل.");
        RuleForEach(x => x.ApprovedCategories).IsInEnum();
        RuleFor(x => x.CoverageGovernorateIds).NotEmpty().WithMessage("اختر محافظة واحدة على الأقل لنطاق التغطية.");
    }
}

/// <summary>
/// Removing a category also deactivates the provider's active offerings in it (kept, not
/// deleted, so they can be switched back on if the category is approved again); the timeline
/// records how many.
/// </summary>
public sealed class UpdateServiceProviderByAdminCommandHandler
    : IRequestHandler<UpdateServiceProviderByAdminCommand, AdminServiceProviderDetailDto>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceOfferingRepository _offerings;
    private readonly IServiceProviderEventRepository _events;
    private readonly IGovernorateLookup _governorates;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;
    private readonly ProviderAdminDetailBuilder _builder;

    public UpdateServiceProviderByAdminCommandHandler(
        IServiceProviderRepository providers,
        IServiceOfferingRepository offerings,
        IServiceProviderEventRepository events,
        IGovernorateLookup governorates,
        IUnitOfWork uow,
        IAuditLogService audit,
        ProviderAdminDetailBuilder builder)
    {
        _providers = providers;
        _offerings = offerings;
        _events = events;
        _governorates = governorates;
        _uow = uow;
        _audit = audit;
        _builder = builder;
    }

    public async Task<AdminServiceProviderDetailDto> Handle(UpdateServiceProviderByAdminCommand request, CancellationToken ct)
    {
        var provider = await _providers.GetByIdAsync(request.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        await ProviderOnboardingRules.EnsureKnownGovernoratesAsync(_governorates, request.CoverageGovernorateIds.ToArray(), ct);

        var now = DateTime.UtcNow;
        var before = new
        {
            providerId = provider.Id,
            categories = provider.ApprovedCategories,
            governorates = provider.CoverageGovernorateIds,
        };

        var removed = ServiceCategorySet.FromNames(provider.ApprovedCategories)
            .Except(request.ApprovedCategories)
            .ToHashSet();

        provider.UpdateProfile(request.DisplayName, request.Bio, request.ContactEmail, request.ContactPhone, now);
        provider.SetApprovedScope(request.ApprovedCategories, request.CoverageGovernorateIds, now);
        provider.UpdatedByUserId = request.AdminUserId;

        var deactivated = 0;
        if (removed.Count > 0)
        {
            foreach (var offering in await _offerings.GetByProviderIdAsync(provider.Id, ct))
            {
                if (offering.IsActive && removed.Contains(offering.Category))
                {
                    offering.Deactivate(now);
                    deactivated++;
                }
            }
        }

        var note = ProviderAdminAudit.DescribeScope(request.ApprovedCategories, request.CoverageGovernorateIds)
            + (deactivated > 0 ? $"; deactivatedOfferings={deactivated}" : string.Empty);
        await _events.AddAsync(ServiceProviderEvent.Record(
            null, provider.Id, request.AdminUserId, ServiceProviderEventType.ProviderUpdated, note, isInternal: true, now), ct);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(
            request.AdminUserId, AuditActions.ServiceProviderUpdatedByAdmin, request.IpAddress,
            ProviderAdminAudit.Json(before),
            ProviderAdminAudit.Json(new
            {
                providerId = provider.Id,
                categories = provider.ApprovedCategories,
                governorates = provider.CoverageGovernorateIds,
                deactivatedOfferings = deactivated,
            }),
            ct);

        return await _builder.BuildAsync(provider.Id, ct);
    }
}

// ── Suspend / reactivate / revoke ────────────────────────────────────────

public enum ServiceProviderStatusAction
{
    Suspend = 0,
    Reactivate = 1,
    Revoke = 2,
}

public sealed record ChangeServiceProviderStatusCommand(
    Guid AdminUserId,
    Guid ServiceProviderId,
    ServiceProviderStatusAction Action,
    string? Reason,
    string? IpAddress) : IRequest<AdminServiceProviderDetailDto>;

public sealed class ChangeServiceProviderStatusCommandValidator : AbstractValidator<ChangeServiceProviderStatusCommand>
{
    public ChangeServiceProviderStatusCommandValidator()
    {
        RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("السبب مطلوب.")
            .When(x => x.Action != ServiceProviderStatusAction.Reactivate);
        RuleFor(x => x.Reason).MaximumLength(2000);
    }
}

public sealed class ChangeServiceProviderStatusCommandHandler
    : IRequestHandler<ChangeServiceProviderStatusCommand, AdminServiceProviderDetailDto>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceProviderEventRepository _events;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;
    private readonly INotificationService _notifications;
    private readonly ILogger<ChangeServiceProviderStatusCommandHandler> _logger;
    private readonly ProviderAdminDetailBuilder _builder;

    public ChangeServiceProviderStatusCommandHandler(
        IServiceProviderRepository providers,
        IServiceProviderEventRepository events,
        IUnitOfWork uow,
        IAuditLogService audit,
        INotificationService notifications,
        ILogger<ChangeServiceProviderStatusCommandHandler> logger,
        ProviderAdminDetailBuilder builder)
    {
        _providers = providers;
        _events = events;
        _uow = uow;
        _audit = audit;
        _notifications = notifications;
        _logger = logger;
        _builder = builder;
    }

    public async Task<AdminServiceProviderDetailDto> Handle(ChangeServiceProviderStatusCommand request, CancellationToken ct)
    {
        var provider = await _providers.GetByIdAsync(request.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        var now = DateTime.UtcNow;
        var before = provider.Status;
        var reason = request.Reason?.Trim() ?? string.Empty;

        var (eventType, auditAction, messageKey) = request.Action switch
        {
            ServiceProviderStatusAction.Suspend => (ServiceProviderEventType.ProviderSuspended,
                AuditActions.ServiceProviderSuspendedByAdmin, NotificationKeys.ProviderSuspended),
            ServiceProviderStatusAction.Reactivate => (ServiceProviderEventType.ProviderReactivated,
                AuditActions.ServiceProviderReactivatedByAdmin, NotificationKeys.ProviderReactivated),
            _ => (ServiceProviderEventType.ProviderRevoked,
                AuditActions.ServiceProviderRevokedByAdmin, NotificationKeys.ProviderRevoked),
        };

        switch (request.Action)
        {
            case ServiceProviderStatusAction.Suspend: provider.Suspend(reason, now); break;
            case ServiceProviderStatusAction.Reactivate: provider.Reactivate(now); break;
            default: provider.Revoke(reason, now); break;
        }

        provider.UpdatedByUserId = request.AdminUserId;
        await _events.AddAsync(ServiceProviderEvent.Record(
            null, provider.Id, request.AdminUserId, eventType,
            string.IsNullOrWhiteSpace(reason) ? null : reason, isInternal: false, now), ct);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(
            request.AdminUserId, auditAction, request.IpAddress,
            ProviderAdminAudit.Json(new { providerId = provider.Id, status = before.ToString() }),
            ProviderAdminAudit.Json(new { providerId = provider.Id, status = provider.Status.ToString() }),
            ct);

        await ProviderOnboardingNotifier.TryNotifyAsync(
            _notifications, _logger, provider.UserId, NotificationType.ServiceProviderStatusChanged,
            NotificationText.Of(messageKey,
                ("name", NotificationText.FromLiteral(provider.DisplayName)),
                ("reason", NotificationText.FromLiteral(reason))),
            provider.Id, ct);

        return await _builder.BuildAsync(provider.Id, ct);
    }
}

/// <summary>Builds the admin provider page; registered in DI by the Application layer.</summary>
public sealed class ProviderAdminDetailBuilder
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceOfferingRepository _offerings;
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceReviewRepository _reviews;
    private readonly IServiceProviderApplicationRepository _applications;
    private readonly IServiceProviderEventRepository _events;

    public ProviderAdminDetailBuilder(
        IServiceProviderRepository providers,
        IServiceOfferingRepository offerings,
        IServiceRequestRepository requests,
        IServiceReviewRepository reviews,
        IServiceProviderApplicationRepository applications,
        IServiceProviderEventRepository events)
    {
        _providers = providers;
        _offerings = offerings;
        _requests = requests;
        _reviews = reviews;
        _applications = applications;
        _events = events;
    }

    public async Task<AdminServiceProviderDetailDto> BuildAsync(Guid serviceProviderId, CancellationToken ct)
    {
        var provider = await _providers.GetByIdAsync(serviceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        var (average, count) = await _reviews.GetRatingSummaryAsync(provider.Id, ct);
        var offerings = await _offerings.GetByProviderIdAsync(provider.Id, ct);
        var openRequests = await _requests.CountOpenByServiceProviderIdAsync(provider.Id, ct);
        var applications = await _applications.ListByApplicantAsync(provider.UserId, ct);
        var timeline = await _events.ListForProviderAsync(provider.Id, ct);

        return new AdminServiceProviderDetailDto(
            ServiceMapper.ToDto(provider, average, count),
            openRequests,
            offerings.Select(ServiceMapper.ToDto).ToList(),
            applications.Select(ProviderOnboardingMapper.ToSummary).ToList(),
            timeline.Select(e => ProviderOnboardingMapper.ToDto(e, forApplicant: false)).ToList());
    }
}
