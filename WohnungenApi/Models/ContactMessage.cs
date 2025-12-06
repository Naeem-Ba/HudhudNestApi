using System;

namespace WohnungenApi.Models
{
    public class ContactMessage
    {
        public int Id { get; set; }
        public bool IsRead { get; set; } = false;
        public string? Name { get; set; } = "";
        public string? Email { get; set; } = "";
        public string? Subject { get; set; } = "";
        public string? Message { get; set; } = "";
        public DateTime? SentAt { get; set; } = DateTime.UtcNow;
    }
}
