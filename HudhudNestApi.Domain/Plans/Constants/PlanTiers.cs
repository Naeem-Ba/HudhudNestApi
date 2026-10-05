namespace HudhudNestApi.Domain.Plans.Constants;

/// <summary>
/// The machine-readable tier identifiers seeded by PlansSeed. They were repeated as local
/// string literals in every place that had to single out the free tier
/// (ListingQuotaPolicy's fallback, the agency eligibility policy); one constant means a
/// renamed tier cannot stay correct in one of them and silently wrong in the other.
/// </summary>
public static class PlanTiers
{
    public const string Free = "free";
    public const string Basic = "basic";
    public const string Premium = "premium";
    public const string Elite = "elite";
}
