namespace HudhudNestApi.Application.ShortStay.DTOs;

/// <summary>A date range the host has marked as not available (From inclusive, To exclusive, like a booking).</summary>
public sealed record UnitBlockDto(Guid Id, Guid UnitId, DateOnly From, DateOnly To);
