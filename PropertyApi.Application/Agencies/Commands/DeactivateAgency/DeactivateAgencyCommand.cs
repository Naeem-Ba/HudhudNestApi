using MediatR;

namespace PropertyApi.Application.Agencies.Commands.DeactivateAgency;

/// <summary>
/// "Delete" on the public API, mapped to the agency's existing reversible Deactivate() —
/// see RELEASE-BLOCKERS-AR.md B-4. Hides the public page and blocks new members; touches
/// neither members' listings nor their membership, and is reversible by reactivating.
///
/// A true irreversible delete (detaching members, clearing listing attribution via
/// IAgencyRepository.ClearAgencyAttributionAsync — already present, unused, kept for that
/// future work) is a separate, larger product decision and is deliberately not this.
/// </summary>
public sealed record DeactivateAgencyCommand(
    Guid AgencyId,
    Guid RequestingUserId) : IRequest<Unit>;
