using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.ShortStay.Enums;

namespace PropertyApi.Domain.ShortStay.Entities;

/// <summary>
/// Admin-set verification badge for a host. Phone/Email badges are deliberately NOT stored
/// here — they are read live from the user's existing identity/phone-verification state.
/// Only the four badges with no automated verification pipeline (yet) are tracked here.
/// </summary>
public class HostVerificationRecord : BaseEntity
{
    public Guid UserId { get; set; }
    public HostVerificationType Type { get; set; }
    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;
    public Guid VerifiedByAdminId { get; set; }
}
