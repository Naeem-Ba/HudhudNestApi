namespace HudhudNestApi.Application.Listings.DTOs;

/// <summary>
/// What an owner is told when they ask to extend one expired listing.
///
/// This is a quote and an outstanding charge — NOT a completed payment. The listing is
/// not extended by receiving this; it is extended when the matching Transaction reaches
/// Completed. See RequestListingExtensionCommandHandler for why that separation is load
/// bearing while no payment gateway exists.
/// </summary>
public sealed record ListingExtensionQuoteDto(
    Guid TransactionId,
    Guid PropertyId,
    decimal AmountUsd,
    int ExtensionDays,

    /// <summary>When the listing expired (or will expire).</summary>
    DateTime? ExpiresAt,

    /// <summary>
    /// The deadline after which the listing is deleted and paying no longer recovers it.
    /// Null when the listing has not expired yet.
    /// </summary>
    DateTime? GraceEndsAt,

    /// <summary>
    /// True when this quote reuses a fee the owner already owed rather than adding a new
    /// one. Lets the client say "you already have a pending payment" instead of implying a
    /// second charge.
    /// </summary>
    bool IsExistingPendingFee);
