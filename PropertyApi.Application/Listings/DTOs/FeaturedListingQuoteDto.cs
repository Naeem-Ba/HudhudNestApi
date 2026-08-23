namespace PropertyApi.Application.Listings.DTOs;

/// <summary>
/// What an owner is told when they ask to promote one listing to featured placement.
///
/// This is a quote and an outstanding charge — NOT a completed payment, and the listing is
/// NOT promoted by receiving it. Promotion happens when the matching Transaction reaches
/// Completed. See RequestFeaturedListingCommandHandler for why that separation is load
/// bearing while no payment gateway exists.
/// </summary>
public sealed record FeaturedListingQuoteDto(
    Guid TransactionId,
    Guid PropertyId,
    decimal AmountUsd,
    int FeaturedDays,

    /// <summary>
    /// When the listing's current featured placement ends, if it already has one. A second
    /// purchase adds to this rather than replacing it, so the client can say "your
    /// placement will run until X" instead of implying the remaining days are forfeited.
    /// </summary>
    DateTime? CurrentFeaturedUntil,

    /// <summary>
    /// Where the placement would end once this fee is settled — CurrentFeaturedUntil plus
    /// one featured period, or one period from now if the listing is not featured today.
    ///
    /// Projected from "now", so it moves if the owner pays later. The client must present
    /// it as an estimate, not a promise.
    /// </summary>
    DateTime ProjectedFeaturedUntil,

    /// <summary>
    /// True when this quote reuses a fee the owner already owed rather than adding a new
    /// one. Lets the client say "you already have a pending payment" instead of implying a
    /// second charge.
    /// </summary>
    bool IsExistingPendingFee);
