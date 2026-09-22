using System.ComponentModel.DataAnnotations;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Listings.DTOs;

/// <summary>
/// Public request body for <c>POST /api/properties/{id}/share-events</c>. The four Utm* fields
/// are optional (Phase 1 clients that don't send them keep working unchanged) and are
/// independently re-validated/sanitized server-side regardless of the [StringLength] check here
/// — see <see cref="HudhudNestApi.Domain.Listings.Entities.PropertyShareEvent"/>.SanitizeUtmField.
/// </summary>
public sealed record PropertyShareEventSubmitDto(
    [Required] SharePlatform Platform,
    [StringLength(60)] string? UtmSource = null,
    [StringLength(60)] string? UtmMedium = null,
    [StringLength(60)] string? UtmCampaign = null,
    [StringLength(60)] string? UtmContent = null);
