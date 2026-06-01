using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
namespace WohnungenApi.Models
{
    public class Benutzer : IdentityUser<int>
    {
        public string? DisplayName { get; set; }
        public bool? IsAgent { get; set; }
        public string? Name { get; set; }
        public string? Vorname { get; set; }
        // Email, PhoneNumber, PasswordHash, SecurityStamp, etc. ستأتي من IdentityUser
        public string? TaxNumber { get; set; }
        // Role سيتم إدارته عبر IdentityRole
        public string? ImageUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        // Anzahle_Wohnungen سيتم إزالته وحسابه عند الطلب
        public ICollection<Wohnung> Wohnungen { get; set; } = new List<Wohnung>();
        public ICollection<Messages> Messages { get; set; } = new List<Messages>();
        public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    }
}
