using MediatR;

namespace HudhudNestApi.Application.Agencies.Commands.ReactivateAgency;

/// <summary>
/// Owner-only reversal of DeactivateAgencyCommand, over the existing Agency.Activate().
/// Deactivation was always documented as reversible, but no route led back until now.
/// Idempotent: activating an already-active agency is not an error.
/// </summary>
public sealed record ReactivateAgencyCommand(
    Guid AgencyId,
    Guid RequestingUserId) : IRequest<Unit>;
