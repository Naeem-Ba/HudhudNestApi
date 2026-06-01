using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using WohnungenApi.Domain.Listings.Entities;
using WohnungenApi.Domain.Messaging.Entities;

namespace WohnungenApi.Domain.Users.Entities;

/// <summary>
/// Platform user. Extends IdentityUser with Guid PK.
/// Roles (Admin, Agent, Tenant, Owner) managed via IdentityRole — not a bool flag.
/// </summary>
public class User : IdentityUser<Guid>
{
    // ── Personal Info ──────────────────────────────────────────
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    // ── Agent-specific ─────────────────────────────────────────
    public bool IsAgent { get; set; } = false;

    /// <summary>
    /// Tax/VAT number for agents/owners.
    /// ⚠️ SECURITY: Encrypt this column at the application level
    ///    using IDataProtector before persisting to DB.
    /// </summary>
    public string? TaxNumber { get; set; }

    // ── Profile ────────────────────────────────────────────────
    public string? ProfileImageUrl { get; set; }

    // ── Localization (global support) ──────────────────────────
    /// <summary>BCP-47 language tag, e.g. "en", "de", "ar".</summary>
    public string PreferredLanguage { get; set; } = "en";

    /// <summary>ISO 4217 currency code, e.g. "EUR", "USD", "SYP".</summary>
    public string PreferredCurrency { get; set; } = "EUR";

    /// <summary>ISO 3166-1 alpha-2 country code, e.g. "DE", "SY", "US".</summary>
    public string? CountryCode { get; set; }

    // ── Timestamps ─────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Soft Delete ────────────────────────────────────────────
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // ── Navigation ─────────────────────────────────────────────
    public ICollection<Property> Properties { get; set; } = new List<Property>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
 
}

