namespace HudhudNestApi.Domain.Moderation.Enums;

/// <summary>
/// Why a listing was reported. Stored by name (never by the text a user sees), so a reason can be
/// renamed in any language without touching the data, and a new reason is one new value here plus
/// its translations: the client builds its list from GET /api/listing-reports/reasons.
/// Never renumber or rename an existing value; rows in the database refer to it by name.
/// </summary>
public enum ListingReportReason
{
    Fraud = 1,
    IncorrectDetails = 2,
    FakeListing = 3,
    MisleadingPrice = 4,
    FakePhotos = 5,
    CopyrightViolation = 6,
    Duplicate = 7,
    NoLongerAvailable = 8,
    MisleadingContact = 9,
    Impersonation = 10,
    IllegalContent = 11,
    OffensiveContent = 12,
    OffPlatformPayment = 13,
    Other = 14,
}
