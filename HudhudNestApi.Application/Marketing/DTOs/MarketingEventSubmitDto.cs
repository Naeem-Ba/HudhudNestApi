using System.ComponentModel.DataAnnotations;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Marketing.DTOs;

/// <summary>Public request body for <c>POST /api/marketing-events</c>.</summary>
public sealed record MarketingEventSubmitDto(
    [Required] MarketingEventType EventType,
    [StringLength(120)] string? Campaign,
    [StringLength(64)] string? SessionId,
    [StringLength(300)] string? Path,
    Guid? LeadId,
    Guid? OfferId);
