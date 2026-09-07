namespace PropertyApi.Application.Users.DTOs;

/// <summary>Client-facing projection of a ConsentRecord — proof the caller can review or export.</summary>
public sealed record ConsentRecordDto(
    Guid Id,
    string PolicyType,
    string PolicyVersion,
    DateTime ConsentedAtUtc,
    string Source,
    DateTime? WithdrawnAtUtc,
    bool IsActive);
