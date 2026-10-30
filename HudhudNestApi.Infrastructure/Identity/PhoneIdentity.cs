using System.Linq.Expressions;
using HudhudNestApi.Infrastructure.Identity.Entities;

namespace HudhudNestApi.Infrastructure.Identity;

/// <summary>
/// How an account is found by its phone number without storing the number in clear text
/// (DB security audit 2026-10-29, S01; privacy-gaps P0-2).
///
/// The number itself lives only in <c>Users.PhoneNumber</c>, encrypted. Lookups go through
/// <c>PhoneNumberLookupHash</c> (HMAC-SHA256 with a server secret). <c>NormalizedPhoneNumber</c>
/// is no longer written; it is read only as a fallback for an old row whose hash has not been
/// filled in yet (see PhoneNumberLookupHashBackfill), and the migration
/// NormalizeSchemaIntegrity clears it wherever a hash exists.
///
/// A phone-registered account used its number as the Identity user name, which also put the
/// number into the access token. Its user name is now <c>ph-</c> followed by the lookup hash.
/// </summary>
public static class PhoneIdentity
{
    public const string UserNamePrefix = "ph-";

    public static string UserNameFor(string lookupHash) => UserNamePrefix + lookupHash;

    /// <summary>
    /// True when the user name was derived from the phone number: the new <c>ph-</c> form, or the
    /// legacy form where it was the E.164 number itself.
    /// </summary>
    public static bool HasPhoneUserName(ApplicationUser user) =>
        user.UserName is { } name &&
        (name.StartsWith(UserNamePrefix, StringComparison.Ordinal) ||
         (user.NormalizedPhoneNumber is not null && string.Equals(name, user.NormalizedPhoneNumber, StringComparison.Ordinal)) ||
         LooksLikeE164(name));

    /// <summary>Matches the account holding this number: by hash, or by the legacy column when the row has no hash yet.</summary>
    public static Expression<Func<ApplicationUser, bool>> Holds(string lookupHash, string normalizedPhone) =>
        user => user.PhoneNumberLookupHash == lookupHash ||
                (user.PhoneNumberLookupHash == null && user.NormalizedPhoneNumber == normalizedPhone);

    /// <summary>In-memory form of <see cref="Holds"/>.</summary>
    public static bool HoldsNumber(ApplicationUser user, string lookupHash, string normalizedPhone) =>
        string.Equals(user.PhoneNumberLookupHash, lookupHash, StringComparison.Ordinal) ||
        (user.PhoneNumberLookupHash is null &&
         string.Equals(user.NormalizedPhoneNumber, normalizedPhone, StringComparison.Ordinal));

    /// <summary>
    /// Clears every clear-text copy of the number on rows that already have a lookup hash: the
    /// legacy NormalizedPhoneNumber column, and a user name that is a phone number. Idempotent.
    /// Used by the migration and again after the hash backfill.
    /// </summary>
    public const string ClearPlaintextSql = """
        UPDATE "Users"
        SET "UserName" = 'ph-' || "PhoneNumberLookupHash",
            "NormalizedUserName" = upper('ph-' || "PhoneNumberLookupHash")
        WHERE "PhoneNumberLookupHash" IS NOT NULL
          AND ("UserName" = "NormalizedPhoneNumber" OR ("UserName" ~ '^\+[0-9]+$' AND length("UserName") BETWEEN 7 AND 16))
          AND NOT EXISTS (SELECT 1 FROM "Users" o WHERE o."NormalizedUserName" = upper('ph-' || "Users"."PhoneNumberLookupHash"));

        UPDATE "Users"
        SET "NormalizedPhoneNumber" = NULL
        WHERE "PhoneNumberLookupHash" IS NOT NULL AND "NormalizedPhoneNumber" IS NOT NULL;
        """;

    private static bool LooksLikeE164(string value) =>
        value.Length is >= 7 and <= 16 && value[0] == '+' && value.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
}
