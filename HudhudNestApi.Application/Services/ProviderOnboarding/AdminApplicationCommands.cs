using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Services;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.ProviderOnboarding;

// Every command here is admin-only: ServiceProviderAdminController carries
// [Authorize(Roles = Admin)] on the class, and the acting admin's id always comes from the token.

// ── Queue and detail ─────────────────────────────────────────────────────

public sealed record SearchServiceProviderApplicationsQuery(ServiceProviderApplicationFilter Filter)
    : IRequest<ServicePageDto<AdminServiceProviderApplicationSummaryDto>>;

public sealed class SearchServiceProviderApplicationsQueryHandler
    : IRequestHandler<SearchServiceProviderApplicationsQuery, ServicePageDto<AdminServiceProviderApplicationSummaryDto>>
{
    private readonly IServiceProviderApplicationRepository _applications;

    public SearchServiceProviderApplicationsQueryHandler(IServiceProviderApplicationRepository applications)
        => _applications = applications;

    public async Task<ServicePageDto<AdminServiceProviderApplicationSummaryDto>> Handle(
        SearchServiceProviderApplicationsQuery request, CancellationToken ct)
    {
        var filter = ProviderAdminPaging.Normalize(request.Filter);
        var (items, total) = await _applications.SearchAsync(filter, ct);
        return new(items.Select(ProviderOnboardingMapper.ToSummary).ToList(), total, filter.Page, filter.PageSize);
    }
}

public sealed record GetServiceProviderApplicationForAdminQuery(Guid ApplicationId)
    : IRequest<AdminServiceProviderApplicationDetailDto>;

public sealed class GetServiceProviderApplicationForAdminQueryHandler
    : IRequestHandler<GetServiceProviderApplicationForAdminQuery, AdminServiceProviderApplicationDetailDto>
{
    private readonly IServiceProviderApplicationRepository _applications;
    private readonly IServiceProviderEventRepository _events;
    private readonly IServiceProviderRepository _providers;
    private readonly IUserAccountRepository _users;

    public GetServiceProviderApplicationForAdminQueryHandler(
        IServiceProviderApplicationRepository applications,
        IServiceProviderEventRepository events,
        IServiceProviderRepository providers,
        IUserAccountRepository users)
    {
        _applications = applications;
        _events = events;
        _providers = providers;
        _users = users;
    }

    public async Task<AdminServiceProviderApplicationDetailDto> Handle(
        GetServiceProviderApplicationForAdminQuery request, CancellationToken ct)
    {
        var application = await _applications.GetByIdAsync(request.ApplicationId, ct)
            ?? throw new NotFoundException("طلب الانضمام غير موجود.");

        return await ProviderAdminViews.ApplicationDetailAsync(application, _events, _providers, _users, ct);
    }
}

// ── Notes and info requests ──────────────────────────────────────────────

public sealed record AddServiceProviderApplicationNoteCommand(
    Guid AdminUserId, Guid ApplicationId, string Note, bool VisibleToApplicant)
    : IRequest<AdminServiceProviderApplicationDetailDto>;

public sealed class AddServiceProviderApplicationNoteCommandValidator
    : AbstractValidator<AddServiceProviderApplicationNoteCommand>
{
    public AddServiceProviderApplicationNoteCommandValidator()
    {
        RuleFor(x => x.Note).NotEmpty().MaximumLength(ServiceProviderApplication.MaxMessageLength);
    }
}

public sealed class AddServiceProviderApplicationNoteCommandHandler
    : IRequestHandler<AddServiceProviderApplicationNoteCommand, AdminServiceProviderApplicationDetailDto>
{
    private readonly IServiceProviderApplicationRepository _applications;
    private readonly IServiceProviderEventRepository _events;
    private readonly IServiceProviderRepository _providers;
    private readonly IUserAccountRepository _users;
    private readonly IUnitOfWork _uow;

    public AddServiceProviderApplicationNoteCommandHandler(
        IServiceProviderApplicationRepository applications,
        IServiceProviderEventRepository events,
        IServiceProviderRepository providers,
        IUserAccountRepository users,
        IUnitOfWork uow)
    {
        _applications = applications;
        _events = events;
        _providers = providers;
        _users = users;
        _uow = uow;
    }

    public async Task<AdminServiceProviderApplicationDetailDto> Handle(
        AddServiceProviderApplicationNoteCommand request, CancellationToken ct)
    {
        var application = await _applications.GetByIdAsync(request.ApplicationId, ct)
            ?? throw new NotFoundException("طلب الانضمام غير موجود.");

        await _events.AddAsync(ServiceProviderEvent.Record(
            application.Id, application.ServiceProviderId, request.AdminUserId, ServiceProviderEventType.Note,
            request.Note, isInternal: !request.VisibleToApplicant, DateTime.UtcNow), ct);
        await _uow.SaveChangesAsync(ct);

        return await ProviderAdminViews.ApplicationDetailAsync(application, _events, _providers, _users, ct);
    }
}

public sealed record RequestServiceProviderApplicationInfoCommand(
    Guid AdminUserId, Guid ApplicationId, string Message, string? IpAddress)
    : IRequest<AdminServiceProviderApplicationDetailDto>;

public sealed class RequestServiceProviderApplicationInfoCommandValidator
    : AbstractValidator<RequestServiceProviderApplicationInfoCommand>
{
    public RequestServiceProviderApplicationInfoCommandValidator()
    {
        RuleFor(x => x.Message).NotEmpty().WithMessage("اكتب ما المطلوب من مقدّم الطلب.")
            .MaximumLength(ServiceProviderApplication.MaxMessageLength);
    }
}

public sealed class RequestServiceProviderApplicationInfoCommandHandler
    : IRequestHandler<RequestServiceProviderApplicationInfoCommand, AdminServiceProviderApplicationDetailDto>
{
    private readonly IServiceProviderApplicationRepository _applications;
    private readonly IServiceProviderEventRepository _events;
    private readonly IServiceProviderRepository _providers;
    private readonly IUserAccountRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;
    private readonly INotificationService _notifications;
    private readonly ILogger<RequestServiceProviderApplicationInfoCommandHandler> _logger;

    public RequestServiceProviderApplicationInfoCommandHandler(
        IServiceProviderApplicationRepository applications,
        IServiceProviderEventRepository events,
        IServiceProviderRepository providers,
        IUserAccountRepository users,
        IUnitOfWork uow,
        IAuditLogService audit,
        INotificationService notifications,
        ILogger<RequestServiceProviderApplicationInfoCommandHandler> logger)
    {
        _applications = applications;
        _events = events;
        _providers = providers;
        _users = users;
        _uow = uow;
        _audit = audit;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<AdminServiceProviderApplicationDetailDto> Handle(
        RequestServiceProviderApplicationInfoCommand request, CancellationToken ct)
    {
        var application = await _applications.GetByIdAsync(request.ApplicationId, ct)
            ?? throw new NotFoundException("طلب الانضمام غير موجود.");

        var now = DateTime.UtcNow;
        var before = application.Status;
        application.RequestInfo(request.Message, request.AdminUserId, now);
        application.UpdatedByUserId = request.AdminUserId;
        await _events.AddAsync(ServiceProviderEvent.Record(
            application.Id, null, request.AdminUserId, ServiceProviderEventType.InfoRequested,
            request.Message, isInternal: false, now), ct);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(
            request.AdminUserId, AuditActions.ServiceProviderApplicationInfoRequestedByAdmin, request.IpAddress,
            ProviderAdminAudit.Json(new { applicationId = application.Id, status = before.ToString() }),
            ProviderAdminAudit.Json(new { applicationId = application.Id, status = application.Status.ToString() }),
            ct);

        await ProviderOnboardingNotifier.TryNotifyAsync(
            _notifications, _logger, application.ApplicantUserId,
            NotificationType.ServiceProviderApplicationInfoRequested,
            NotificationText.Of(NotificationKeys.ProviderApplicationInfoRequested,
                ("ref", application.ReferenceNumber), ("message", NotificationText.FromLiteral(request.Message.Trim()))),
            application.Id, ct);

        return await ProviderAdminViews.ApplicationDetailAsync(application, _events, _providers, _users, ct);
    }
}

// ── Approve ──────────────────────────────────────────────────────────────

public sealed record ApproveServiceProviderApplicationCommand(
    Guid AdminUserId,
    Guid ApplicationId,
    IReadOnlyList<ServiceCategory> ApprovedCategories,
    IReadOnlyList<int> ApprovedGovernorateIds,
    string? DisplayName,
    ServiceProviderVerificationLevel? VerificationLevel,
    string? Note,
    string? IpAddress) : IRequest<AdminServiceProviderApplicationDetailDto>;

public sealed class ApproveServiceProviderApplicationCommandValidator
    : AbstractValidator<ApproveServiceProviderApplicationCommand>
{
    public ApproveServiceProviderApplicationCommandValidator()
    {
        RuleFor(x => x.ApprovedCategories).NotEmpty().WithMessage("اختر نوع خدمة معتمدًا واحدًا على الأقل.");
        RuleForEach(x => x.ApprovedCategories).IsInEnum();
        RuleFor(x => x.ApprovedGovernorateIds).NotEmpty().WithMessage("اختر محافظة واحدة على الأقل لنطاق التغطية.");
        RuleFor(x => x.DisplayName).MaximumLength(ServiceProviderApplication.MaxDisplayNameLength);
        RuleFor(x => x.VerificationLevel).IsInEnum().When(x => x.VerificationLevel is not null);
        RuleFor(x => x.Note).MaximumLength(ServiceProviderApplication.MaxMessageLength);
    }
}

/// <summary>
/// The real approval. In one transaction, under an advisory lock keyed on the application:
/// re-read the application, create the provider profile or link the applicant's existing one
/// (never a second profile: the partial unique index on ServiceProviders.UserId backs this up),
/// set the approved categories and governorates, mark the application Approved, record the
/// timeline and the audit row. A retry or a double click after success returns the same result
/// instead of failing or duplicating anything. The applicant is notified only after commit.
/// </summary>
public sealed class ApproveServiceProviderApplicationCommandHandler
    : IRequestHandler<ApproveServiceProviderApplicationCommand, AdminServiceProviderApplicationDetailDto>
{
    private readonly IServiceProviderApplicationRepository _applications;
    private readonly IServiceProviderEventRepository _events;
    private readonly IServiceProviderRepository _providers;
    private readonly IUserAccountRepository _users;
    private readonly IGovernorateLookup _governorates;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;
    private readonly INotificationService _notifications;
    private readonly ILogger<ApproveServiceProviderApplicationCommandHandler> _logger;

    public ApproveServiceProviderApplicationCommandHandler(
        IServiceProviderApplicationRepository applications,
        IServiceProviderEventRepository events,
        IServiceProviderRepository providers,
        IUserAccountRepository users,
        IGovernorateLookup governorates,
        IUnitOfWork uow,
        IAuditLogService audit,
        INotificationService notifications,
        ILogger<ApproveServiceProviderApplicationCommandHandler> logger)
    {
        _applications = applications;
        _events = events;
        _providers = providers;
        _users = users;
        _governorates = governorates;
        _uow = uow;
        _audit = audit;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<AdminServiceProviderApplicationDetailDto> Handle(
        ApproveServiceProviderApplicationCommand request, CancellationToken ct)
    {
        ServiceProviderApplication application;
        bool approvedNow;

        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireAdvisoryLockAsync(ProviderAdminAudit.LockKey(request.ApplicationId), ct);

            application = await _applications.GetByIdAsync(request.ApplicationId, ct)
                ?? throw new NotFoundException("طلب الانضمام غير موجود.");

            if (application.Status == ServiceProviderApplicationStatus.Approved)
            {
                // Idempotent replay: the first approval already did everything.
                approvedNow = false;
            }
            else
            {
                await ApproveAsync(application, request, ct);
                approvedNow = true;
            }

            await _uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(ct);
            throw;
        }

        if (approvedNow)
        {
            await ProviderOnboardingNotifier.TryNotifyAsync(
                _notifications, _logger, application.ApplicantUserId,
                NotificationType.ServiceProviderApplicationApproved,
                NotificationText.Of(NotificationKeys.ProviderApplicationApproved, ("ref", application.ReferenceNumber)),
                application.Id, ct);
        }

        return await ProviderAdminViews.ApplicationDetailAsync(application, _events, _providers, _users, ct);
    }

    private async Task ApproveAsync(
        ServiceProviderApplication application, ApproveServiceProviderApplicationCommand request, CancellationToken ct)
    {
        if (!application.IsOpen)
            throw new ConflictException($"لا يمكن اعتماد طلب حالته {application.Status}.");

        var requestedCategories = ServiceCategorySet.FromNames(application.RequestedCategories);
        var outsideCategories = request.ApprovedCategories.Distinct().Except(requestedCategories).ToList();
        if (outsideCategories.Count > 0)
            throw new ConflictException(
                "يمكن اعتماد الفئات التي طلبها مقدّم الطلب فقط. أضف فئات أخرى لاحقًا من ملف المزوّد.");

        var outsideGovernorates = request.ApprovedGovernorateIds.Distinct().Except(application.RequestedGovernorateIds).ToList();
        if (outsideGovernorates.Count > 0)
            throw new ConflictException(
                "يمكن اعتماد المحافظات التي طلبها مقدّم الطلب فقط. وسّع التغطية لاحقًا من ملف المزوّد.");

        await ProviderOnboardingRules.EnsureKnownGovernoratesAsync(_governorates, request.ApprovedGovernorateIds.ToArray(), ct);

        var account = await _users.GetByIdAsync(application.ApplicantUserId, ct);
        if (account is null || account.IsDeleted || account.DeletionRequestedAt is not null)
            throw new ConflictException("حساب مقدّم الطلب محذوف أو قيد الحذف، فلا يمكن ربط ملف مزوّد به.");

        var now = DateTime.UtcNow;
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? application.DisplayName : request.DisplayName.Trim();

        var provider = await _providers.GetByUserIdAsync(application.ApplicantUserId, ct);
        var created = provider is null;
        if (provider is null)
        {
            provider = ServiceProvider.Create(
                application.ApplicantUserId, agencyId: null, displayName, bio: null,
                application.ContactEmail, application.ContactPhone, now);
            provider.CreatedByUserId = request.AdminUserId;
            await _providers.AddAsync(provider, ct);
        }
        else if (provider.Status == ServiceProviderStatus.Active)
        {
            throw new ConflictException("لمقدّم الطلب ملف مزوّد نشط بالفعل. عدّل فئاته ومناطقه من ملف المزوّد.");
        }

        var previousStatus = provider.Status;
        provider.SetApprovedScope(request.ApprovedCategories, request.ApprovedGovernorateIds, now);
        provider.ActivateByApproval(now);
        if (request.VerificationLevel is { } level)
            provider.SetVerificationLevel(level, now);
        provider.UpdatedByUserId = request.AdminUserId;

        application.Approve(provider.Id, request.AdminUserId, now);
        application.UpdatedByUserId = request.AdminUserId;

        var scope = ProviderAdminAudit.DescribeScope(request.ApprovedCategories, request.ApprovedGovernorateIds);
        await _events.AddAsync(ServiceProviderEvent.Record(
            application.Id, provider.Id, request.AdminUserId, ServiceProviderEventType.ApplicationApproved,
            request.Note, isInternal: false, now), ct);
        await _events.AddAsync(ServiceProviderEvent.Record(
            application.Id, provider.Id, request.AdminUserId,
            created ? ServiceProviderEventType.ProviderCreated : ServiceProviderEventType.ProviderLinked,
            scope, isInternal: true, now), ct);

        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(
            request.AdminUserId, AuditActions.ServiceProviderApplicationApprovedByAdmin, request.IpAddress,
            ProviderAdminAudit.Json(new
            {
                applicationId = application.Id,
                providerId = created ? (Guid?)null : provider.Id,
                providerStatus = created ? null : previousStatus.ToString(),
            }),
            ProviderAdminAudit.Json(new
            {
                applicationId = application.Id,
                providerId = provider.Id,
                created,
                categories = provider.ApprovedCategories,
                governorates = provider.CoverageGovernorateIds,
                verificationLevel = provider.VerificationLevel.ToString(),
            }),
            ct);
    }
}

// ── Reject ───────────────────────────────────────────────────────────────

public sealed record RejectServiceProviderApplicationCommand(
    Guid AdminUserId, Guid ApplicationId, string Reason, string? IpAddress)
    : IRequest<AdminServiceProviderApplicationDetailDto>;

public sealed class RejectServiceProviderApplicationCommandValidator
    : AbstractValidator<RejectServiceProviderApplicationCommand>
{
    public RejectServiceProviderApplicationCommandValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("سبب الرفض مطلوب.")
            .MaximumLength(ServiceProviderApplication.MaxMessageLength);
    }
}

public sealed class RejectServiceProviderApplicationCommandHandler
    : IRequestHandler<RejectServiceProviderApplicationCommand, AdminServiceProviderApplicationDetailDto>
{
    private readonly IServiceProviderApplicationRepository _applications;
    private readonly IServiceProviderEventRepository _events;
    private readonly IServiceProviderRepository _providers;
    private readonly IUserAccountRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;
    private readonly INotificationService _notifications;
    private readonly ILogger<RejectServiceProviderApplicationCommandHandler> _logger;

    public RejectServiceProviderApplicationCommandHandler(
        IServiceProviderApplicationRepository applications,
        IServiceProviderEventRepository events,
        IServiceProviderRepository providers,
        IUserAccountRepository users,
        IUnitOfWork uow,
        IAuditLogService audit,
        INotificationService notifications,
        ILogger<RejectServiceProviderApplicationCommandHandler> logger)
    {
        _applications = applications;
        _events = events;
        _providers = providers;
        _users = users;
        _uow = uow;
        _audit = audit;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<AdminServiceProviderApplicationDetailDto> Handle(
        RejectServiceProviderApplicationCommand request, CancellationToken ct)
    {
        var application = await _applications.GetByIdAsync(request.ApplicationId, ct)
            ?? throw new NotFoundException("طلب الانضمام غير موجود.");

        var now = DateTime.UtcNow;
        var before = application.Status;
        application.Reject(request.Reason, request.AdminUserId, now);
        application.UpdatedByUserId = request.AdminUserId;
        await _events.AddAsync(ServiceProviderEvent.Record(
            application.Id, null, request.AdminUserId, ServiceProviderEventType.ApplicationRejected,
            request.Reason, isInternal: false, now), ct);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(
            request.AdminUserId, AuditActions.ServiceProviderApplicationRejectedByAdmin, request.IpAddress,
            ProviderAdminAudit.Json(new { applicationId = application.Id, status = before.ToString() }),
            ProviderAdminAudit.Json(new { applicationId = application.Id, status = application.Status.ToString() }),
            ct);

        await ProviderOnboardingNotifier.TryNotifyAsync(
            _notifications, _logger, application.ApplicantUserId,
            NotificationType.ServiceProviderApplicationRejected,
            NotificationText.Of(NotificationKeys.ProviderApplicationRejected,
                ("ref", application.ReferenceNumber), ("reason", NotificationText.FromLiteral(request.Reason.Trim()))),
            application.Id, ct);

        return await ProviderAdminViews.ApplicationDetailAsync(application, _events, _providers, _users, ct);
    }
}

// ── Shared helpers ───────────────────────────────────────────────────────

internal static class ProviderAdminPaging
{
    public static ServiceProviderApplicationFilter Normalize(ServiceProviderApplicationFilter f) =>
        f with { Page = Math.Max(1, f.Page), PageSize = Math.Clamp(f.PageSize, 1, 100) };

    public static ServiceProviderFilter Normalize(ServiceProviderFilter f) =>
        f with { Page = Math.Max(1, f.Page), PageSize = Math.Clamp(f.PageSize, 1, 100) };
}

internal static class ProviderAdminAudit
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Audit snapshots carry ids, statuses and scope only, never contact details.</summary>
    public static string Json(object value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Stable advisory-lock key for one application (pg_advisory_xact_lock takes a bigint).</summary>
    public static long LockKey(Guid id) => BitConverter.ToInt64(id.ToByteArray(), 0) ^ 0x5350_4150_5052_4F56;

    /// <summary>Plain, language-neutral record of an approved scope for the internal timeline.</summary>
    public static string DescribeScope(IEnumerable<ServiceCategory> categories, IEnumerable<int> governorateIds) =>
        $"categories={string.Join(",", ServiceCategorySet.ToNames(categories))}; governorates={string.Join(",", ServiceCategorySet.NormalizeIds(governorateIds))}";
}

internal static class ProviderAdminViews
{
    public static async Task<AdminServiceProviderApplicationDetailDto> ApplicationDetailAsync(
        ServiceProviderApplication application,
        IServiceProviderEventRepository events,
        IServiceProviderRepository providers,
        IUserAccountRepository users,
        CancellationToken ct)
    {
        var timeline = await events.ListForApplicationAsync(application.Id, includeInternal: true, ct);
        var account = await users.GetByIdAsync(application.ApplicantUserId, ct);
        var existing = await providers.GetByUserIdAsync(application.ApplicantUserId, ct);

        var accountName = account is null
            ? "-"
            : string.IsNullOrWhiteSpace(account.DisplayName)
                ? $"{account.FirstName} {account.LastName}".Trim()
                : account.DisplayName!;

        return new AdminServiceProviderApplicationDetailDto(
            ProviderOnboardingMapper.ToSummary(application),
            application.Experience,
            application.CompanyName,
            application.LicenseNumber,
            application.InfoRequest,
            application.DecisionReason,
            application.ReviewedByUserId,
            application.ReviewedAt,
            application.TermsAcceptedAt,
            application.ServiceProviderId,
            accountName,
            account is { IsDeleted: false, DeletionRequestedAt: null },
            existing?.Id,
            existing?.Status,
            timeline.Select(e => ProviderOnboardingMapper.ToDto(e, forApplicant: false)).ToList());
    }
}
