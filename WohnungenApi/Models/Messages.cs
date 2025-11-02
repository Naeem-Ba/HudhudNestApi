using System;

namespace WohnungenApi.Models
{
    public class Messages
    {
        public int Id { get; set; }
        public int WohnungId { get; set; }
        public int? UserId { get; set; }
        public string? SenderName { get; set; }
        public string? SenderEmail { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Wohnung Wohnung { get; set; } = null!;
        public Benutzer? User { get; set; }
    }
}
