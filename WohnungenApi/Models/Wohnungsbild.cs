using System;
namespace WohnungenApi.Models
{
    public class Wohnungsbild
    {
        public int Id { get; set; }
        public int WohnungId { get; set; }
        public string Url { get; set; } = string.Empty;
        public bool IsMain { get; set; }
        public int? SortOrder { get; set; }
        public string? AltText { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Wohnung Wohnung { get; set; } = null!;
    }
}
