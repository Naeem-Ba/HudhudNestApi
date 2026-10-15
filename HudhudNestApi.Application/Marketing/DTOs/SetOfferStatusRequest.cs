using System.ComponentModel.DataAnnotations;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Marketing.DTOs;

/// <summary>Admin request body of <c>PUT /api/offers/{id}/status</c>:
/// <c>{ "status": "Active" | "Paused" | "Ended" }</c>, sent as application/json. (The body used
/// to be a bare JSON string; a bare string is exactly what Angular sends as text/plain, which
/// ASP.NET refuses with 415 — a named property cannot be mistaken for anything else.)</summary>
public sealed record SetOfferStatusRequest(
    [Required]
    OfferStatus? Status);
