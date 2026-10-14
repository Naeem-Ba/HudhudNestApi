using FluentValidation;
using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.ProviderOnboarding;

/// <summary>What the applicant fills in, on submission and again when answering an info request.</summary>
public sealed record ServiceProviderApplicationInput(
    ServiceProviderApplicantType ApplicantType,
    string DisplayName,
    string ContactName,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<ServiceCategory> Categories,
    IReadOnlyList<int> GovernorateIds,
    string Experience,
    string? CompanyName,
    string? LicenseNumber);

public sealed class ServiceProviderApplicationInputValidator : AbstractValidator<ServiceProviderApplicationInput>
{
    public ServiceProviderApplicationInputValidator()
    {
        RuleFor(x => x.ApplicantType).IsInEnum();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(ServiceProviderApplication.MaxDisplayNameLength);
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(ServiceProviderApplication.MaxContactNameLength);
        RuleFor(x => x.ContactEmail)
            .EmailAddress().WithMessage("صيغة البريد الإلكتروني غير صحيحة.")
            .MaximumLength(ServiceProviderApplication.MaxEmailLength)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.ContactPhone)
            .Matches(@"^\+?[0-9\s\-]{7,20}$").WithMessage("صيغة رقم الهاتف غير صحيحة.")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactPhone));
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.ContactEmail) || !string.IsNullOrWhiteSpace(x.ContactPhone))
            .WithName("Contact")
            .WithMessage("أدخل بريدًا إلكترونيًا أو رقم هاتف للتواصل.");
        RuleFor(x => x.Categories).NotEmpty().WithMessage("اختر نوع خدمة واحدًا على الأقل.")
            .Must(c => c.Count <= 13);
        RuleForEach(x => x.Categories).IsInEnum();
        RuleFor(x => x.GovernorateIds).NotEmpty().WithMessage("اختر محافظة واحدة على الأقل.")
            .Must(g => g.Count <= 30);
        RuleFor(x => x.Experience).NotEmpty().MaximumLength(ServiceProviderApplication.MaxExperienceLength);
        RuleFor(x => x.CompanyName).MaximumLength(ServiceProviderApplication.MaxCompanyNameLength);
        RuleFor(x => x.CompanyName).NotEmpty().WithMessage("اسم الشركة مطلوب لمقدّم الطلب من نوع شركة.")
            .When(x => x.ApplicantType == ServiceProviderApplicantType.Company);
        RuleFor(x => x.LicenseNumber).MaximumLength(ServiceProviderApplication.MaxLicenseNumberLength);
    }
}

// ── Submit ───────────────────────────────────────────────────────────────

public sealed record SubmitServiceProviderApplicationCommand(
    Guid ActorUserId,
    ServiceProviderApplicationInput Input,
    bool TermsAccepted) : IRequest<ServiceProviderApplicationDto>;

public sealed class SubmitServiceProviderApplicationCommandValidator
    : AbstractValidator<SubmitServiceProviderApplicationCommand>
{
    public SubmitServiceProviderApplicationCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.Input).NotNull().SetValidator(new ServiceProviderApplicationInputValidator());
        RuleFor(x => x.TermsAccepted).Equal(true).WithMessage("يجب الموافقة على الشروط وسياسة الخصوصية.");
    }
}

public sealed class SubmitServiceProviderApplicationCommandHandler
    : IRequestHandler<SubmitServiceProviderApplicationCommand, ServiceProviderApplicationDto>
{
    private readonly IServiceProviderApplicationRepository _applications;
    private readonly IServiceProviderEventRepository _events;
    private readonly IServiceProviderRepository _providers;
    private readonly IUserAccountRepository _users;
    private readonly IGovernorateLookup _governorates;
    private readonly IUnitOfWork _uow;

    public SubmitServiceProviderApplicationCommandHandler(
        IServiceProviderApplicationRepository applications,
        IServiceProviderEventRepository events,
        IServiceProviderRepository providers,
        IUserAccountRepository users,
        IGovernorateLookup governorates,
        IUnitOfWork uow)
    {
        _applications = applications;
        _events = events;
        _providers = providers;
        _users = users;
        _governorates = governorates;
        _uow = uow;
    }

    public async Task<ServiceProviderApplicationDto> Handle(
        SubmitServiceProviderApplicationCommand request, CancellationToken ct)
    {
        var account = await _users.GetByIdAsync(request.ActorUserId, ct);
        if (account is null || account.IsDeleted || account.DeletionRequestedAt is not null)
            throw new ForbiddenException("هذا الحساب لا يستطيع تقديم طلب انضمام.");

        var provider = await _providers.GetByUserIdAsync(request.ActorUserId, ct);
        if (provider is { Status: ServiceProviderStatus.Active })
            throw new ConflictException("أنت مزوّد خدمة معتمد بالفعل. لتعديل خدماتك أو مناطقك تواصل مع الإدارة.");
        if (provider is { Status: ServiceProviderStatus.Suspended })
            throw new ConflictException("ملف مزوّد الخدمة الخاص بك موقوف حاليًا. تواصل مع الإدارة.");

        if (await _applications.GetOpenByApplicantAsync(request.ActorUserId, ct) is not null)
            throw new ConflictException("لديك طلب انضمام مفتوح بالفعل. تابعه من صفحة الانضمام كمزوّد خدمة.");

        await ProviderOnboardingRules.EnsureKnownGovernoratesAsync(_governorates, request.Input.GovernorateIds, ct);

        var now = DateTime.UtcNow;
        var input = request.Input;
        var application = ServiceProviderApplication.Submit(
            request.ActorUserId, input.ApplicantType, input.DisplayName, input.ContactName,
            input.ContactEmail, input.ContactPhone, input.Categories, input.GovernorateIds,
            input.Experience, input.CompanyName, input.LicenseNumber, request.TermsAccepted, now);
        application.CreatedByUserId = request.ActorUserId;

        await _applications.AddAsync(application, ct);
        var submitted = ServiceProviderEvent.Record(
            application.Id, null, request.ActorUserId, ServiceProviderEventType.ApplicationSubmitted,
            null, isInternal: false, now);
        await _events.AddAsync(submitted, ct);
        await _uow.SaveChangesAsync(ct);

        return ProviderOnboardingMapper.ToApplicantDto(application, [submitted]);
    }
}

// ── Respond to an info request ───────────────────────────────────────────

public sealed record RespondToServiceProviderApplicationCommand(
    Guid ActorUserId,
    Guid ApplicationId,
    ServiceProviderApplicationInput Input,
    string Message) : IRequest<ServiceProviderApplicationDto>;

public sealed class RespondToServiceProviderApplicationCommandValidator
    : AbstractValidator<RespondToServiceProviderApplicationCommand>
{
    public RespondToServiceProviderApplicationCommandValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty();
        RuleFor(x => x.Input).NotNull().SetValidator(new ServiceProviderApplicationInputValidator());
        RuleFor(x => x.Message).NotEmpty().WithMessage("اكتب ردّك على طلب الإدارة.")
            .MaximumLength(ServiceProviderApplication.MaxMessageLength);
    }
}

public sealed class RespondToServiceProviderApplicationCommandHandler
    : IRequestHandler<RespondToServiceProviderApplicationCommand, ServiceProviderApplicationDto>
{
    private readonly IServiceProviderApplicationRepository _applications;
    private readonly IServiceProviderEventRepository _events;
    private readonly IGovernorateLookup _governorates;
    private readonly IUnitOfWork _uow;

    public RespondToServiceProviderApplicationCommandHandler(
        IServiceProviderApplicationRepository applications,
        IServiceProviderEventRepository events,
        IGovernorateLookup governorates,
        IUnitOfWork uow)
    {
        _applications = applications;
        _events = events;
        _governorates = governorates;
        _uow = uow;
    }

    public async Task<ServiceProviderApplicationDto> Handle(
        RespondToServiceProviderApplicationCommand request, CancellationToken ct)
    {
        var application = await ProviderOnboardingRules.GetOwnAsync(
            _applications, request.ApplicationId, request.ActorUserId, ct);

        await ProviderOnboardingRules.EnsureKnownGovernoratesAsync(_governorates, request.Input.GovernorateIds, ct);

        var now = DateTime.UtcNow;
        var input = request.Input;
        application.Respond(
            input.ApplicantType, input.DisplayName, input.ContactName, input.ContactEmail,
            input.ContactPhone, input.Categories, input.GovernorateIds, input.Experience,
            input.CompanyName, input.LicenseNumber, now);
        application.UpdatedByUserId = request.ActorUserId;

        await _events.AddAsync(ServiceProviderEvent.Record(
            application.Id, null, request.ActorUserId, ServiceProviderEventType.ApplicantResponded,
            request.Message, isInternal: false, now), ct);
        await _uow.SaveChangesAsync(ct);

        var events = await _events.ListForApplicationAsync(application.Id, includeInternal: false, ct);
        return ProviderOnboardingMapper.ToApplicantDto(application, events);
    }
}

// ── Withdraw ─────────────────────────────────────────────────────────────

public sealed record WithdrawServiceProviderApplicationCommand(
    Guid ActorUserId, Guid ApplicationId) : IRequest<ServiceProviderApplicationDto>;

public sealed class WithdrawServiceProviderApplicationCommandHandler
    : IRequestHandler<WithdrawServiceProviderApplicationCommand, ServiceProviderApplicationDto>
{
    private readonly IServiceProviderApplicationRepository _applications;
    private readonly IServiceProviderEventRepository _events;
    private readonly IUnitOfWork _uow;

    public WithdrawServiceProviderApplicationCommandHandler(
        IServiceProviderApplicationRepository applications,
        IServiceProviderEventRepository events,
        IUnitOfWork uow)
    {
        _applications = applications;
        _events = events;
        _uow = uow;
    }

    public async Task<ServiceProviderApplicationDto> Handle(
        WithdrawServiceProviderApplicationCommand request, CancellationToken ct)
    {
        var application = await ProviderOnboardingRules.GetOwnAsync(
            _applications, request.ApplicationId, request.ActorUserId, ct);

        var now = DateTime.UtcNow;
        application.Withdraw(now);
        application.UpdatedByUserId = request.ActorUserId;
        await _events.AddAsync(ServiceProviderEvent.Record(
            application.Id, null, request.ActorUserId, ServiceProviderEventType.ApplicationWithdrawn,
            null, isInternal: false, now), ct);
        await _uow.SaveChangesAsync(ct);

        var events = await _events.ListForApplicationAsync(application.Id, includeInternal: false, ct);
        return ProviderOnboardingMapper.ToApplicantDto(application, events);
    }
}

// ── My applications ──────────────────────────────────────────────────────

public sealed record GetMyServiceProviderApplicationsQuery(Guid ActorUserId)
    : IRequest<IReadOnlyList<ServiceProviderApplicationDto>>;

public sealed class GetMyServiceProviderApplicationsQueryHandler
    : IRequestHandler<GetMyServiceProviderApplicationsQuery, IReadOnlyList<ServiceProviderApplicationDto>>
{
    private readonly IServiceProviderApplicationRepository _applications;
    private readonly IServiceProviderEventRepository _events;

    public GetMyServiceProviderApplicationsQueryHandler(
        IServiceProviderApplicationRepository applications, IServiceProviderEventRepository events)
    {
        _applications = applications;
        _events = events;
    }

    public async Task<IReadOnlyList<ServiceProviderApplicationDto>> Handle(
        GetMyServiceProviderApplicationsQuery request, CancellationToken ct)
    {
        var applications = await _applications.ListByApplicantAsync(request.ActorUserId, ct);
        var result = new List<ServiceProviderApplicationDto>(applications.Count);
        foreach (var application in applications)
        {
            var events = await _events.ListForApplicationAsync(application.Id, includeInternal: false, ct);
            result.Add(ProviderOnboardingMapper.ToApplicantDto(application, events));
        }

        return result;
    }
}

internal static class ProviderOnboardingRules
{
    /// <summary>
    /// The caller's own application, or 404. A foreign id answers exactly like a missing one, so
    /// the endpoint cannot be used to discover which application ids exist.
    /// </summary>
    public static async Task<ServiceProviderApplication> GetOwnAsync(
        IServiceProviderApplicationRepository applications, Guid applicationId, Guid actorUserId, CancellationToken ct)
    {
        var application = await applications.GetByIdAsync(applicationId, ct);
        if (application is null || application.ApplicantUserId != actorUserId)
            throw new NotFoundException("طلب الانضمام غير موجود.");

        return application;
    }

    public static async Task EnsureKnownGovernoratesAsync(
        IGovernorateLookup governorates, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToArray();
        var known = await governorates.GetKnownIdsAsync(wanted, ct);
        if (known.Count != wanted.Length)
            throw new Common.Exceptions.ValidationException([new FluentValidation.Results.ValidationFailure(
                "GovernorateIds", "محافظة غير معروفة في نطاق التغطية.")]);
    }
}
