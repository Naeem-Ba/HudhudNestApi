using System.ComponentModel.DataAnnotations;

namespace PropertyApi.Application.Marketing.DTOs;

/// <summary>Public request body for <c>POST /api/leads</c>. No email field — the landing
/// page collects name/phone/city/userType, matching lead.model.ts on the frontend exactly.</summary>
public sealed record LeadSubmitDto(
    [Required]
    [StringLength(150, MinimumLength = 2)]
    string FullName,

    [Required]
    [StringLength(30, MinimumLength = 6)]
    string Phone,

    [Required]
    [StringLength(100, MinimumLength = 1)]
    string City,

    [Required]
    [RegularExpression("^(renter|buyer|owner|agency|investor)$")]
    string UserType,

    [StringLength(1000)]
    string? Notes,

    [StringLength(120)]
    string? Campaign,

    Guid? OfferId);
