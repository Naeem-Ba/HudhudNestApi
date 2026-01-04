using System;
using System.Collections.Generic;
namespace WohnungenApi.Models
{
    public class Benutzer
    {
        public int? Id { get; set; }
        public string? DisplayName { get; set; } = string.Empty;
        public bool? IsAgent { get; set; }
        public string? Name { get; set; }
        public string? Vorname { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string PasswordHash { get; set; }
        public string? TaxNumber { get; set; }
        public string? Role { get; set; } = "user";
        public string? ImageUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int? Anzahle_Wohnungen { get; set; } = 0;
        public ICollection<Wohnung> Wohnungen { get; set; } = new List<Wohnung>();
        public ICollection<Messages> Messages { get; set; } = new List<Messages>();
        public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    }
}
