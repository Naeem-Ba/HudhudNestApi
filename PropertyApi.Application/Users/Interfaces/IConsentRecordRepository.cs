using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Interfaces;

public interface IConsentRecordRepository
{
    void Add(ConsentRecord record);

    /// <summary>All consent records for a user, newest first — includes withdrawn ones.</summary>
    Task<IReadOnlyList<ConsentRecord>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's currently-active (not withdrawn) record for one exact policy version, if any.</summary>
    Task<ConsentRecord?> GetActiveAsync(
        Guid userId,
        ConsentPolicyType policyType,
        string policyVersion,
        CancellationToken ct = default);

    /// <summary>Every currently-active record for a user across all versions of one policy type.</summary>
    Task<IReadOnlyList<ConsentRecord>> GetActiveByTypeAsync(
        Guid userId,
        ConsentPolicyType policyType,
        CancellationToken ct = default);
}
