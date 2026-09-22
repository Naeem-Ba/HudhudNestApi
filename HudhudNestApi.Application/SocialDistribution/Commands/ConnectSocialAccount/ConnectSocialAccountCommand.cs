using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.ConnectSocialAccount;

/// <summary>
/// No real OAuth handshake exists yet (spec §17: no unofficial APIs, no fabricated tokens) — this
/// is the manual-registration path a HudhudNest operator uses once a credential has been
/// provisioned out-of-band in a real secret manager. <paramref name="CredentialReference"/> is
/// that secret's reference/key name, never the secret itself; it is still encrypted at rest
/// (see SocialAccount remarks) in case an operator pastes something more sensitive.
/// </summary>
public sealed record ConnectSocialAccountCommand(Guid AccountId, string? CredentialReference) : IRequest<SocialAccountDto>;
