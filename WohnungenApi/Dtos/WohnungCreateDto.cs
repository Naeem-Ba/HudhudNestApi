using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace WohnungenApi.Dtos
{
    public class WohnungCreateDto
    {
        public string Titel { get; set; } = string.Empty;
        public string Beschreibung { get; set; } = string.Empty;
        public string Adresse { get; set; } = string.Empty;
        public string? Stadt { get; set; }
        public string? PLZ { get; set; }
        public decimal? Kaltmiete { get; set; }
        public bool? ZumMieten { get; set; }
        public decimal? Kaution { get; set; }
        public int? Zimmer { get; set; }
        public double? Flaeche { get; set; }

        public List<IFormFile>? Bilder { get; set; }
    }
}
