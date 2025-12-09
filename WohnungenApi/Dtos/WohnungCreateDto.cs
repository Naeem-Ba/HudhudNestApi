
using WohnungenApi.Models.Enums;

namespace WohnungenApi.Dtos
{
    public class WohnungCreateDto
    {
        public string Titel { get; set; } = "";
        public string Beschreibung { get; set; } = "";
        public string Adresse { get; set; } = "";
        public Stadt? Stadt { get; set; }
        public string? PLZ { get; set; }

        public bool? ZumMieten { get; set; }
        public bool? ZumKaufen { get; set; }

        public decimal? Kaltmiete { get; set; }
        public decimal? Warmmiete { get; set; }
        public decimal? Kaufpreis { get; set; }
        public decimal? Nebenkosten { get; set; }
        public decimal? Kaution { get; set; }

        public int? Zimmer { get; set; }
        public double? Flaeche { get; set; }
        public int? Geschoss { get; set; }

        public DateTime? FreiAb { get; set; }

        public bool? Balkon { get; set; }
        public bool? Aufzug { get; set; }
        public bool? Stellplatz { get; set; }

        public string? Heizung { get; set; }
        public EnergieausweisTyp? Energieausweis { get; set; }
        public WohnungsZustand? Zustand { get; set; }
        public WohnungsStatus? Status { get; set; }

        public int? OwnerId { get; set; }

        public List<IFormFile>? Bilder { get; set; }
    }
}
