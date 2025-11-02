using System.ComponentModel.DataAnnotations.Schema;
using WohnungenApi.Models.Enums;
using System;
using System.Collections.Generic;
namespace WohnungenApi.Models
{
    public class Wohnung
    {
        public int Id { get; set; }
        public string Titel { get; set; } = string.Empty;
        public string Beschreibung { get; set; } = string.Empty;

        public string Adresse { get; set; } = string.Empty;
        public string Stadt { get; set; } = string.Empty;
        public string PLZ { get; set; } = string.Empty;

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public bool ZumMieten { get; set; }
        public bool ZumKaufen { get; set; }

        public decimal? Kaltmiete { get; set; }
        public decimal? Warmmiete { get; set; }
        public decimal? Kaufpreis { get; set; }
        public decimal? Nebenkosten { get; set; }
        public decimal? Kaution { get; set; }

        public int Zimmer { get; set; }
        public double Flaeche { get; set; }
        public int Geschoss { get; set; }
        public DateTime? FreiAb { get; set; }

        public bool Balkon { get; set; }
        public bool Aufzug { get; set; }
        public bool Stellplatz { get; set; }

        public string Heizung { get; set; } = string.Empty;

        // Enums
        public EnergieausweisTyp Energieausweis { get; set; }
        public WohnungsZustand Zustand { get; set; }
        public WohnungsStatus Status { get; set; }

        public int? OwnerId { get; set; }
        public Benutzer? Owner { get; set; }



        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public ICollection<Wohnungsbild> Bilder { get; set; } = new List<Wohnungsbild>();
        public ICollection<Messages> Messages { get; set; } = new List<Messages>();
        public ICollection<Amenity> Amenities { get; set; } = new List<Amenity>();
        public List<WohnungAmenity> WohnungAmenities { get; set; } = new List<WohnungAmenity>();

        public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    }
}
