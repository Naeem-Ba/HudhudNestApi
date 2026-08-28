namespace PropertyApi.Domain.Services.Enums;

/// <summary>
/// How much the platform itself has verified about a ServiceProvider — deliberately graded
/// rather than a single IsVerified boolean, because "we checked their account exists" and
/// "we checked they're a licensed business" are very different claims and must never be
/// collapsed into one badge. Nothing in this codebase may present any level here as a
/// guarantee about a property, a document, or a transaction outcome — it describes only what
/// the platform has checked about the provider's identity/standing.
/// </summary>
public enum ServiceProviderVerificationLevel
{
    /// <summary>No verification performed yet. Default for every new provider.</summary>
    None = 0,

    /// <summary>The underlying UserAccount/login has been verified (e.g. phone verified).</summary>
    AccountVerified = 1,

    /// <summary>A government-issued ID was checked against the provider's declared identity.</summary>
    IdentityVerified = 2,

    /// <summary>Business registration / licence documents were checked.</summary>
    BusinessVerified = 3,

    /// <summary>The platform has directly audited this provider's service delivery quality.</summary>
    ServiceVerified = 4,
}
