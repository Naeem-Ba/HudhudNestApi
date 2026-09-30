namespace HudhudNestApi.Application.Contact.DTOs;

public sealed record ContactMessagesPageDto(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<ContactMessageDto> Data);

