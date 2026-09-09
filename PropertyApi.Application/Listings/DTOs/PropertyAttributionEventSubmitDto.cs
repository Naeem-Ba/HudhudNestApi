using System.ComponentModel.DataAnnotations;
using PropertyApi.Domain.Listings.Enums;

namespace PropertyApi.Application.Listings.DTOs;

/// <summary>
/// Public request body for <c>POST /api/properties/{id}/attribution-events</c>. Every Utm* field
/// is optional — a direct visit (no UTM parameters at all) is a perfectly valid, expected event,
/// not an error. Values are independently re-validated/sanitized server-side regardless of the
/// [StringLength] check here — see PropertyShareEvent.SanitizeUtmField.
/// </summary>
public sealed record PropertyAttributionEventSubmitDto(
    [Required] PropertyAttributionEventType EventType,
    [StringLength(60)] string? UtmSource = null,
    [StringLength(60)] string? UtmMedium = null,
    [StringLength(60)] string? UtmCampaign = null,
    [StringLength(60)] string? UtmContent = null);
