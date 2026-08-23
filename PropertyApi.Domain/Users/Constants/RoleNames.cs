namespace PropertyApi.Domain.Users.Constants;

public static class RoleNames
{
    public const string User = "User";
    public const string Agent = "Agent";
    public const string Admin = "Admin";

    /// <summary>
    /// Owns a real-estate office. The only member who may add or remove other members,
    /// edit the agency profile, or transfer ownership.
    ///
    /// The role says WHAT a user may do; UserAccount.AgencyId says WHICH agency they do
    /// it for. Holding this role without an AgencyId grants nothing — every agency
    /// handler resolves the caller's AgencyId and compares it to the target.
    /// </summary>
    public const string AgencyOwner = "AgencyOwner";

    /// <summary>
    /// Works under an agency. Manages their own listings exactly as any other user and
    /// is shown on the agency's public page. Deliberately NOT able to act on a colleague's
    /// listings: a Property still belongs to its OwnerId, and shared write access across
    /// members would need real per-listing permissions rather than a role name.
    /// </summary>
    public const string AgencyAgent = "AgencyAgent";

    public static readonly string[] All =
    [
        User,
        Agent,
        Admin,
        AgencyOwner,
        AgencyAgent
    ];
}
