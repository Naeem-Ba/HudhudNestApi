namespace PropertyApi.Application.Common.DTOs;

public sealed record AmenityLookupDto(
    string Id,
    string Name,
    string? Category = null,
    string? IconName = null);

