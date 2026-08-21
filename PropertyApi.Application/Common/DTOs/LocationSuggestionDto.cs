namespace PropertyApi.Application.Common.DTOs;

public sealed record LocationSuggestionDto(
    Guid Id,
    string Type,           // "District" | "Neighborhood"
    int ParentId,
    string ParentName,     // resolved governorate/district name, for admin readability
    string Name,
    Guid SubmittedByUserId,
    string? SubmittedByUserName,
    Guid? PropertyId,
    string? PropertyTitle,
    DateTime CreatedAt);
