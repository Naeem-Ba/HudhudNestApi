namespace PropertyApi.Domain.Users;

/// <summary>
/// Validation bounds for admin-chosen subscription durations (activate/extend). Plan
/// itself deliberately carries no billing-cycle field (see Plan's doc comment), so the
/// admin picks the duration explicitly each time — these bounds exist only to reject an
/// obviously-wrong input (0, negative, or an absurdly large number of days), not to encode
/// a real product duration. Same "policy constant lives in Domain, not appsettings"
/// convention as ListingLifecyclePolicy.
/// </summary>
public static class SubscriptionLifecyclePolicy
{
    public const int MinAdminDurationDays = 1;

    /// <summary>~10 years — generous enough for any legitimate grant, tight enough to
    /// catch a unit mistake (e.g. minutes typed where days were meant).</summary>
    public const int MaxAdminDurationDays = 3650;

    /// <summary>Day-preset chips the admin UI offers, alongside a free-form custom value.</summary>
    public static readonly IReadOnlyList<int> SuggestedDurationDaysPresets =
        new[] { 30, 90, 180, 365 };
}
