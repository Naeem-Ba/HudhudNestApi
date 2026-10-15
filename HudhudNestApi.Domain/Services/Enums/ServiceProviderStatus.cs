namespace HudhudNestApi.Domain.Services.Enums;

/// <summary>
/// Whether an approved provider may currently take new work. Separate from the application's
/// status on purpose: the approval decision is history, this is today's standing.
///
/// Active    → Suspended | Revoked
/// Suspended → Active | Revoked
/// Revoked is final for this approval. The person may apply again; approving that new
/// application re-activates the same profile instead of creating a second one.
///
/// Neither Suspended nor Revoked deletes anything: offerings, reviews and existing service
/// requests stay. A provider in either state is hidden from the public catalog and cannot accept
/// new requests, but can still finish (schedule/start/complete) or reject what it already has,
/// so a requester is never left with a request nobody can close.
/// </summary>
public enum ServiceProviderStatus
{
    Active = 0,
    Suspended = 1,
    Revoked = 2,
}
