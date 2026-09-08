namespace PropertyApi.Domain.Marketing.Enums;

/// <summary>
/// Lifecycle of a marketing <c>Offer</c> (e.g. "first 100 agencies"). Only
/// <see cref="Active"/> offers are ever returned by the public "current offer" endpoint —
/// <see cref="Draft"/> lets an admin prepare one before it goes live, <see cref="Paused"/>
/// lets one be hidden temporarily without losing its redemption count, and
/// <see cref="Ended"/> is the terminal state once it expires or is closed manually.
/// </summary>
public enum OfferStatus
{
    Draft = 1,
    Active = 2,
    Paused = 3,
    Ended = 4
}
