using System.ComponentModel.DataAnnotations;

namespace WohnungenApi.Models
{
    public class Wohnungsbild
    {
        public int Id { get; set; }

        [Required]
        public string Url { get; set; } = null!;

        [Required]
        public string PublicId { get; set; } = null!;

        public bool IsMain { get; set; }

        public int WohnungId { get; set; }
        public Wohnung Wohnung { get; set; } = null!;
    }
}