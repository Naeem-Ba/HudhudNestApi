namespace PropertyApi.Application.Services.DTOs;

public sealed record ServiceReviewDocumentDto(
    Guid Id,
    Guid ServiceRequestId,
    string FileUrl,
    string FileType,
    string FileName,
    Guid UploadedByUserId,
    DateTime CreatedAt);
