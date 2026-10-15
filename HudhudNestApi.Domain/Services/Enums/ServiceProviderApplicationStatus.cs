namespace HudhudNestApi.Domain.Services.Enums;

/// <summary>
/// Lifecycle of a request to join the marketplace as a ServiceProvider. This is the status of the
/// APPLICATION only; whether an approved provider is currently allowed to work is
/// <see cref="ServiceProviderStatus"/> on the provider profile, so suspending a provider never
/// rewrites the historical decision on the application that created it.
///
/// Pending   → NeedsInfo | Approved | Rejected | Withdrawn
/// NeedsInfo → Pending (applicant answered) | Approved | Rejected | Withdrawn
/// Approved, Rejected, Withdrawn are final. A rejected or withdrawn applicant may submit a new
/// application; the old one stays as history.
///
/// Persisted as a string (HasConversion&lt;string&gt;()): never rename a value once shipped.
/// </summary>
public enum ServiceProviderApplicationStatus
{
    Pending = 0,
    NeedsInfo = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4,
}
