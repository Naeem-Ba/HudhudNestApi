namespace PropertyApi.Domain.Users.Entities;

/// <summary>
/// Business profile of a platform user.
///
/// Contains profile, localization, and business information only.
/// Authentication and account-security state belong to the Identity layer.
/// </summary>
public sealed class UserAccount
{
    private UserAccount()
    {
    }

    public Guid Id { get; private set; }

    public string FirstName { get; private set; } =
        string.Empty;

    public string LastName { get; private set; } =
        string.Empty;

    public string? DisplayName { get; private set; }

    public string? TaxNumber { get; private set; }

    public string? ProfileImageUrl { get; private set; }

    /// <summary>
    /// معرّف الصورة عند مزوّد التخزين (Cloudinary PublicId حاليًا) — يُخزَّن كي
    /// نستطيع حذف الصورة القديمة من التخزين السحابي عند رفع صورة جديدة، بنفس
    /// النمط المُستخدَم لصور العقارات (PropertyImage.PublicId). يبقى null عندما
    /// يكون ProfileImageUrl قد وصل من مصدر خارجي غير مُتتبَّع عبر IMediaStorageService.
    /// </summary>
    public string? ProfileImagePublicId { get; private set; }

    public string? WhatsAppNumber { get; private set; }

    public string PreferredLanguage { get; private set; } =
        "en";

    public string PreferredCurrency { get; private set; } =
        "EUR";

    public string? CountryCode { get; private set; }

    /// <summary>نبذة تعريفية يكتبها صاحب الحساب عن نفسه/عمله — تُعرض بالملف الشخصي العام.</summary>
    public string? Bio { get; private set; }

    /// <summary>عنوان ومعلومات تواصل حرة (نص) يختار المستخدم عرضها بملفه العام.</summary>
    public string? ContactInfo { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static UserAccount Create(
        Guid id,
        string firstName,
        string lastName,
        DateTime utcNow)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "User account id is required.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException(
                "First name is required.",
                nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException(
                "Last name is required.",
                nameof(lastName));
        }

        return new UserAccount
        {
            Id =
                id,

            FirstName =
                firstName.Trim(),

            LastName =
                lastName.Trim(),

            CreatedAt =
                utcNow,

            UpdatedAt =
                utcNow
        };
    }

    public void UpdateProfile(
        string firstName,
        string lastName,
        string? displayName,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException(
                "First name is required.",
                nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException(
                "Last name is required.",
                nameof(lastName));
        }

        FirstName =
            firstName.Trim();

        LastName =
            lastName.Trim();

        DisplayName =
            string.IsNullOrWhiteSpace(displayName)
                ? null
                : displayName.Trim();

        UpdatedAt =
            utcNow;
    }

    public void UpdateProfileImage(
        string? profileImageUrl,
        string? profileImagePublicId,
        DateTime utcNow)
    {
        ProfileImageUrl =
            string.IsNullOrWhiteSpace(profileImageUrl)
                ? null
                : profileImageUrl.Trim();

        ProfileImagePublicId =
            string.IsNullOrWhiteSpace(profileImagePublicId)
                ? null
                : profileImagePublicId.Trim();

        UpdatedAt =
            utcNow;
    }

    /// <summary>
    /// يحدّث النبذة التعريفية ومعلومات العنوان/التواصل — نفس نمط "null تعني
    /// لا تغيير" المُستخدَم بالفعل في UpdateUserCommandHandler، لكن هنا القيمة
    /// المُرسَلة نفسها تُحدَّد صراحةً من الطبقة الأعلى (المُتصل مسؤول عن حساب
    /// effectiveBio/effectiveContactInfo قبل الاستدعاء)، فهذه الدالة لا تفرّق
    /// بين null و"مسح الحقل" — أي قيمة فارغة أو بيضاء هنا تُخزَّن كـ null فعليًا.
    /// </summary>
    public void UpdateAboutInfo(
        string? bio,
        string? contactInfo,
        DateTime utcNow)
    {
        Bio =
            string.IsNullOrWhiteSpace(bio)
                ? null
                : bio.Trim();

        ContactInfo =
            string.IsNullOrWhiteSpace(contactInfo)
                ? null
                : contactInfo.Trim();

        UpdatedAt =
            utcNow;
    }

    public void UpdatePreferences(
        string preferredLanguage,
        string preferredCurrency,
        string? countryCode,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(
                preferredLanguage))
        {
            throw new ArgumentException(
                "Preferred language is required.",
                nameof(preferredLanguage));
        }

        if (string.IsNullOrWhiteSpace(
                preferredCurrency))
        {
            throw new ArgumentException(
                "Preferred currency is required.",
                nameof(preferredCurrency));
        }

        PreferredLanguage =
            preferredLanguage
                .Trim()
                .ToLowerInvariant();

        PreferredCurrency =
            preferredCurrency
                .Trim()
                .ToUpperInvariant();

        CountryCode =
            string.IsNullOrWhiteSpace(countryCode)
                ? null
                : countryCode
                    .Trim()
                    .ToUpperInvariant();

        UpdatedAt =
            utcNow;
    }
}