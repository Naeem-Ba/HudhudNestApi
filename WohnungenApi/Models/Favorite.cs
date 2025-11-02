using System;

namespace WohnungenApi.Models
{
    public class Favorite
    {
        public int UserId { get; set; }
        public int WohnungId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Benutzer User { get; set; } = null!;
        public Wohnung Wohnung { get; set; } = null!;
    }
}
