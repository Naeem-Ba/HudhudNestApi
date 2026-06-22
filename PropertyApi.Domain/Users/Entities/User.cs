using Microsoft.AspNetCore.Identity;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Messaging.Entities;

namespace PropertyApi.Domain.Users.Entities;

/// <summary>
/// Platform user. Extends IdentityUser with Guid PK.
/// Roles (Admin, Agent, User) are managed through IdentityRole, not boolean flags.
/// </summary>
public class User : IdentityUser<Guid>
{
    // -- Personal Info ------------------------------------------
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    /// <summary>
    /// Tax/VAT number for agents/owners.
    /// SECURITY: Encrypt this column at the application level
    /// using IDataProtector before persisting to DB.
    /// </summary>
    public string? TaxNumber { get; set; }

    // -- Profile ------------------------------------------------
    public string? ProfileImageUrl { get; set; }

    // -- Localization (global support) --------------------------
    /// <summary>BCP-47 language tag, e.g. "en", "de", "ar".</summary>
    public string PreferredLanguage { get; set; } = "en";

    /// <summary>ISO 4217 currency code, e.g. "EUR", "USD", "SYP".</summary>
    public string PreferredCurrency { get; set; } = "EUR";

    /// <summary>ISO 3166-1 alpha-2 country code, e.g. "DE", "SY", "US".</summary>
    public string? CountryCode { get; set; }

    // -- Timestamps ---------------------------------------------
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // -- Soft Delete --------------------------------------------
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // -- حقول جديدة للسوق السوري ---------------------------

    /// <summary>
    /// رقم واتساب (قد يختلف عن PhoneNumber).
    /// في سوريا واتساب هو وسيلة التواصل الأساسية للسمسرة.
    /// </summary>
    public string? WhatsAppNumber { get; set; }

    /// <summary>هل الحساب محظور من قِبل الإدارة؟</summary>
    public bool IsBanned { get; set; } = false;

    /// <summary>سبب الحظر (يُعرض للمستخدم عند محاولة تسجيل الدخول)</summary>
    public string? BanReason { get; set; }

    /// <summary>تاريخ آخر تسجيل دخول — لتتبع النشاط</summary>
    public DateTime? LastLoginAt { get; set; }

    // -- Navigation ---------------------------------------------
    public ICollection<Property> Properties { get; set; } = new List<Property>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
}
