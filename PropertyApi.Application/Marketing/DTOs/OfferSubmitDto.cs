using System.ComponentModel.DataAnnotations;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Application.Marketing.DTOs;

/// <summary>Admin request body for creating/updating an Offer.</summary>
public sealed record OfferSubmitDto(
    [Required]
    [StringLength(150, MinimumLength = 2)]
    string Name,

    [StringLength(1000)]
    string? Description,

    [Required]
    OfferDiscountType DiscountType,

    [Range(0.01, 1_000_000)]
    decimal DiscountValue,

    [StringLength(30)]
    string? TargetPlanTier,

    [Required]
    DateTime StartsAtUtc,

    DateTime? EndsAtUtc,

    [Range(1, int.MaxValue)]
    int? MaxRedemptions,

    [StringLength(2000)]
    string? Terms);
