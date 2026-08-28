namespace PropertyApi.Domain.Services.Enums;

/// <summary>
/// The full taxonomy of service categories the marketplace is designed to grow into.
///
/// MVP scope (AqarTech Services Marketplace, 2026): only <see cref="Verification"/> is built
/// as a working, seeded, end-to-end product this vertical slice. Valuation, Inspection,
/// Photography and PropertyManagement are the next four MVP services and are reserved here
/// (stable enum values reused later, nothing renumbered) but have no seeded provider/offering
/// yet. Everything from <see cref="Legal"/> onward is future/extensible scope only — present
/// so ServiceRequest.Category never needs a breaking rename, not something exposed anywhere
/// in the UI yet.
///
/// Persisted via HasConversion&lt;string&gt;() (the codebase's majority enum-storage
/// convention) — so values are safe to reorder in this file, but never rename once shipped.
/// </summary>
public enum ServiceCategory
{
    /// <summary>AqarTech Verify — document/ownership verification for a listing.</summary>
    Verification = 1,

    /// <summary>AqarTech Valuation — market-price estimation for a property.</summary>
    Valuation = 2,

    /// <summary>AqarTech Inspect — physical condition inspection.</summary>
    Inspection = 3,

    /// <summary>AqarTech Media — professional photography/media production.</summary>
    Photography = 4,

    /// <summary>AqarTech Care — remote property management for owners abroad.</summary>
    PropertyManagement = 5,

    Legal = 6,
    Engineering = 7,
    Maintenance = 8,
    Cleaning = 9,
    Moving = 10,
    Financing = 11,
    Insurance = 12,
    Investment = 13,
}
