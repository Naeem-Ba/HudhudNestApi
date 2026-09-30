namespace HudhudNestApi.Application.Services.DTOs;

public sealed record ServiceRequestDocumentDto(
    Guid Id,
    Guid ServiceRequestId,
    string FileUrl,
    string FileType,
    string FileName,
    Guid UploadedByUserId,
    DateTime CreatedAt);
