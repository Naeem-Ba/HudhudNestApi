using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Identity.Entities;

/// <summary>
/// JWT refresh token with full security audit trail.
/// ?? SECURITY: Consider storing only a SHA-256 hash of the token,
///    not the plain token, to prevent DB breach exposure.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The token value (or its hash — see security note above).</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }

    /// <summary>Token that replaced this one (rotation chain).</summary>
    public string? ReplacedByToken { get; set; }

    /// <summary>IP address that created this token.</summary>
    public string? CreatedByIp { get; set; }

    /// <summary>IP address that revoked this token (helps detect theft).</summary>
    public string? RevokedByIp { get; set; }

    // FK
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    // Helper
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsActive => !IsRevoked && !IsExpired;
}
