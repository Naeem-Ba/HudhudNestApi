namespace HudhudNestApi.Domain.Services.Enums;

/// <summary>
/// The CUSTOMER's own reason when a service did not happen (or when they cancel). Kept apart
/// from <see cref="ServiceOutcomeReason"/> on purpose: the two sides often disagree, and that
/// disagreement is the signal the admin wants to see.
/// </summary>
public enum ServiceRequesterReason
{
    ProviderDidNotContact = 1,
    ProviderNoShow = 2,
    PriceTooHigh = 3,
    ChangedMind = 4,
    FoundOtherProvider = 5,
    NoLongerNeeded = 6,
    TimingNotSuitable = 7,
    ServiceNotAsDescribed = 8,
    QualityIssue = 9,
    Other = 99,
}
