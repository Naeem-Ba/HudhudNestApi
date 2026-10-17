namespace HudhudNestApi.Domain.Services.Enums;

/// <summary>
/// Why the PROVIDER did not deliver (or declined) a service request. A closed list, not free
/// text, so the admin can count and compare reasons; the free-text note travels beside it.
/// Stored as its name (string column), so members can be added but never renamed.
/// </summary>
public enum ServiceOutcomeReason
{
    CustomerUnreachable = 1,
    CustomerNotAvailable = 2,
    CustomerCancelled = 3,
    PriceNotAgreed = 4,
    PropertyNotAccessible = 5,
    OutsideCoverage = 6,
    ProviderUnavailable = 7,
    ScopeNotSuitable = 8,
    Other = 99,
}
