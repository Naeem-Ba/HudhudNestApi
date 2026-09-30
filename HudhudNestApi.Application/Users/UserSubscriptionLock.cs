namespace HudhudNestApi.Application.Users;

/// <summary>
/// Derives a PostgreSQL advisory-lock key for one account's subscription — serializes
/// concurrent admin actions (activate/extend/cancel) on the same UserAccount so two admins
/// racing each other cannot both read the same "current PlanExpiresAt" and both add their
/// extension on top of it, silently dropping one. Same fold+XOR-tag construction as
/// ListingQuotaLock/AgencyInvitationLock, for the same collision-avoidance reason.
/// </summary>
public static class UserSubscriptionLock
{
    private const long DomainTag = 0x5553_5542_534B_5931; // "USSUBSK1" (ASCII bytes)

    public static long ForUser(Guid userId) => Fold(userId) ^ DomainTag;

    private static long Fold(Guid id) => BitConverter.ToInt64(id.ToByteArray(), 0);
}
