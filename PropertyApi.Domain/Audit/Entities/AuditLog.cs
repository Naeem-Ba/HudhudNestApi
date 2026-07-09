using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Domain.Audit.Entities;

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User that performed the action. Nullable only to keep system-level events possible.
    /// For the required security events this should be populated.
    /// </summary>
    public Guid? UserId { get; set; }
    public UserAccount? User { get; set; }

    public string Action { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// JSON/text snapshot before the operation. Never store secrets or plaintext passwords here.
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// JSON/text snapshot after the operation. Never store secrets or plaintext passwords here.
    /// </summary>
    public string? NewValue { get; set; }
}
