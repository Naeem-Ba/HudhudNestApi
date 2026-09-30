namespace HudhudNestApi.Application.Contact.DTOs;

public sealed record ContactMessageDto(
    Guid Id,
    string Name,
    string Email,
    string? Subject,
    string Body,
    bool IsRead,
    DateTime CreatedAt);

