namespace HudhudNestApi.Application.Users.Messaging.DTOs;

public sealed class MessageDto
{
    public Guid Id { get; set; }
    public Guid PropertyId { get; set; }
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public string SenderDisplayName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

