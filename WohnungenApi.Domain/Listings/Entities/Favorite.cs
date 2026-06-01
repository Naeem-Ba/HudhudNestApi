using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WohnungenApi.Domain.Users.Entities;

namespace WohnungenApi.Domain.Listings.Entities;

/// <summary>
/// User's saved/favorited properties.
/// Composite PK: (UserId, PropertyId) — no surrogate Id needed.
/// </summary>
public class Favorite
{
    public Guid UserId { get; set; }
    public Guid PropertyId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;  // ✅ UTC

    public User User { get; set; } = null!;
    public Property Property { get; set; } = null!;
}

