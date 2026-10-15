using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Domain.Services.Entities;

/// <summary>
/// A signed-in user's request to offer services on the marketplace. Replaces the old route of
/// sending a free-text contact message, which carried no account, no categories, no areas and no
/// status, so an admin could read it but had nothing to approve.
///
/// The applicant must be signed in: the approved ServiceProvider profile is owned by an account
/// (ServiceProvider.UserId), and tying the application to that account from the start is what
/// lets the applicant follow it and answer questions without anyone else seeing it.
///
/// Transitions are enforced here and nowhere else (see <see cref="ServiceProviderApplicationStatus"/>).
/// </summary>
public sealed class ServiceProviderApplication : AuditableEntity
{
    public const int MaxDisplayNameLength = 150;
    public const int MaxContactNameLength = 150;
    public const int MaxEmailLength = 320;
    public const int MaxPhoneLength = 50;
    public const int MaxExperienceLength = 3000;
    public const int MaxCompanyNameLength = 200;
    public const int MaxLicenseNumberLength = 100;
    public const int MaxMessageLength = 2000;

    private ServiceProviderApplication() { }

    /// <summary>Database-assigned sequence number behind <see cref="ReferenceNumber"/>.</summary>
    public long Number { get; private set; }

    /// <summary>What the applicant and the admin quote, e.g. SPA-000042.</summary>
    public string ReferenceNumber => $"SPA-{Number:D6}";

    public Guid ApplicantUserId { get; private set; }

    public ServiceProviderApplicantType ApplicantType { get; private set; }

    /// <summary>The name the provider would be listed under.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    public string ContactName { get; private set; } = string.Empty;

    public string? ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    /// <summary>ServiceCategory names (see <see cref="ServiceCategorySet"/>).</summary>
    public string[] RequestedCategories { get; private set; } = [];

    /// <summary>Governorate ids the applicant says they cover.</summary>
    public int[] RequestedGovernorateIds { get; private set; } = [];

    public string Experience { get; private set; } = string.Empty;

    public string? CompanyName { get; private set; }

    public string? LicenseNumber { get; private set; }

    public ServiceProviderApplicationStatus Status { get; private set; }

    /// <summary>The latest question the admin asked while the application is NeedsInfo.</summary>
    public string? InfoRequest { get; private set; }

    /// <summary>The reason shown to the applicant on rejection.</summary>
    public string? DecisionReason { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTime? ReviewedAt { get; private set; }

    /// <summary>The provider profile this application created or was linked to on approval.</summary>
    public Guid? ServiceProviderId { get; private set; }

    public DateTime TermsAcceptedAt { get; private set; }

    public bool IsOpen => Status is ServiceProviderApplicationStatus.Pending or ServiceProviderApplicationStatus.NeedsInfo;

    public static ServiceProviderApplication Submit(
        Guid applicantUserId,
        ServiceProviderApplicantType applicantType,
        string displayName,
        string contactName,
        string? contactEmail,
        string? contactPhone,
        IEnumerable<ServiceCategory> categories,
        IEnumerable<int> governorateIds,
        string experience,
        string? companyName,
        string? licenseNumber,
        bool termsAccepted,
        DateTime utcNow)
    {
        if (applicantUserId == Guid.Empty)
            throw new DomainException("لا يمكن تقديم طلب انضمام بلا حساب.");

        if (!termsAccepted)
            throw new DomainException("يجب الموافقة على الشروط وسياسة الخصوصية.");

        var application = new ServiceProviderApplication
        {
            ApplicantUserId = applicantUserId,
            Status = ServiceProviderApplicationStatus.Pending,
            TermsAcceptedAt = utcNow,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };

        application.SetDetails(
            applicantType, displayName, contactName, contactEmail, contactPhone,
            categories, governorateIds, experience, companyName, licenseNumber);

        return application;
    }

    /// <summary>Admin asks the applicant for more information. Allowed while the application is open.</summary>
    public void RequestInfo(string message, Guid adminUserId, DateTime utcNow)
    {
        EnsureOpen("طلب معلومات إضافية");

        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException("اكتب ما المطلوب من مقدّم الطلب.");

        Status = ServiceProviderApplicationStatus.NeedsInfo;
        InfoRequest = message.Trim();
        ReviewedByUserId = adminUserId;
        ReviewedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>The applicant updates the application in answer to an info request; it goes back to review.</summary>
    public void Respond(
        ServiceProviderApplicantType applicantType,
        string displayName,
        string contactName,
        string? contactEmail,
        string? contactPhone,
        IEnumerable<ServiceCategory> categories,
        IEnumerable<int> governorateIds,
        string experience,
        string? companyName,
        string? licenseNumber,
        DateTime utcNow)
    {
        if (Status != ServiceProviderApplicationStatus.NeedsInfo)
            throw new InvalidStateTransitionException("لا يمكن تعديل الطلب إلا بعد أن تطلب الإدارة معلومات إضافية.");

        SetDetails(
            applicantType, displayName, contactName, contactEmail, contactPhone,
            categories, governorateIds, experience, companyName, licenseNumber);

        Status = ServiceProviderApplicationStatus.Pending;
        UpdatedAt = utcNow;
    }

    public void Approve(Guid serviceProviderId, Guid adminUserId, DateTime utcNow)
    {
        EnsureOpen("الاعتماد");

        if (serviceProviderId == Guid.Empty)
            throw new DomainException("الاعتماد يحتاج ملف مزوّد خدمة.");

        Status = ServiceProviderApplicationStatus.Approved;
        ServiceProviderId = serviceProviderId;
        DecisionReason = null;
        ReviewedByUserId = adminUserId;
        ReviewedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void Reject(string reason, Guid adminUserId, DateTime utcNow)
    {
        EnsureOpen("الرفض");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("سبب الرفض مطلوب.");

        Status = ServiceProviderApplicationStatus.Rejected;
        DecisionReason = reason.Trim();
        ReviewedByUserId = adminUserId;
        ReviewedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void Withdraw(DateTime utcNow)
    {
        EnsureOpen("السحب");
        Status = ServiceProviderApplicationStatus.Withdrawn;
        UpdatedAt = utcNow;
    }

    private void EnsureOpen(string action)
    {
        if (!IsOpen)
            throw new InvalidStateTransitionException($"لا يمكن تنفيذ {action}: الطلب مغلق ({Status}).");
    }

    private void SetDetails(
        ServiceProviderApplicantType applicantType,
        string displayName,
        string contactName,
        string? contactEmail,
        string? contactPhone,
        IEnumerable<ServiceCategory> categories,
        IEnumerable<int> governorateIds,
        string experience,
        string? companyName,
        string? licenseNumber)
    {
        if (!Enum.IsDefined(applicantType))
            throw new DomainException("نوع مقدّم الخدمة غير صالح.");

        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("الاسم الذي ستظهر به مطلوب.");

        if (string.IsNullOrWhiteSpace(contactName))
            throw new DomainException("اسم الشخص المسؤول مطلوب.");

        if (string.IsNullOrWhiteSpace(contactEmail) && string.IsNullOrWhiteSpace(contactPhone))
            throw new DomainException("أدخل بريدًا إلكترونيًا أو رقم هاتف للتواصل.");

        var categoryNames = ServiceCategorySet.ToNames(categories);
        if (categoryNames.Length == 0)
            throw new DomainException("اختر نوع خدمة واحدًا على الأقل.");

        var governorates = ServiceCategorySet.NormalizeIds(governorateIds);
        if (governorates.Length == 0)
            throw new DomainException("اختر محافظة واحدة على الأقل.");

        if (string.IsNullOrWhiteSpace(experience))
            throw new DomainException("صف خبرتك وتخصصك.");

        if (applicantType == ServiceProviderApplicantType.Company && string.IsNullOrWhiteSpace(companyName))
            throw new DomainException("اسم الشركة مطلوب لمقدّم الطلب من نوع شركة.");

        ApplicantType = applicantType;
        DisplayName = displayName.Trim();
        ContactName = contactName.Trim();
        ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim().ToLowerInvariant();
        ContactPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim();
        RequestedCategories = categoryNames;
        RequestedGovernorateIds = governorates;
        Experience = experience.Trim();
        CompanyName = string.IsNullOrWhiteSpace(companyName) ? null : companyName.Trim();
        LicenseNumber = string.IsNullOrWhiteSpace(licenseNumber) ? null : licenseNumber.Trim();
    }
}
