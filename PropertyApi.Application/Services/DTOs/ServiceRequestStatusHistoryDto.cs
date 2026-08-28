using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Application.Services.DTOs;

public sealed record ServiceRequestStatusHistoryDto(
    Guid Id,
    ServiceRequestStatus? FromStatus,
    ServiceRequestStatus ToStatus,
    Guid? ChangedByUserId,
    string? Note,
    DateTime CreatedAt);
