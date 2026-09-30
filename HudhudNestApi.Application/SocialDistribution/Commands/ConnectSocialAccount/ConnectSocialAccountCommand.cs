using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.ConnectSocialAccount;

/// <summary>
/// No real OAuth handshake exists yet (spec §17: no unofficial APIs, no fabricated tokens) — this
/// is the manual-registration path an AqarTech operator uses once a credential has been
/// provisioned out-of-band (a bot token from @BotFather, a Page/Instagram access token from Meta
/// for Developers, ...). <paramref name="CredentialReference"/> IS that real credential value for
/// this specific account (Phase 3 — see <see cref="Infrastructure.SocialDistribution.Publishing.TelegramBotPublisher"/>
/// and its Facebook/Instagram siblings' own remarks on how they resolve it), stored encrypted at
/// rest (see SocialAccount remarks) — never logged, never returned by any query
/// (<see cref="SocialAccountDto"/> never includes it), and never sent anywhere but this admin
/// endpoint over HTTPS.
/// </summary>
public sealed record ConnectSocialAccountCommand(Guid AccountId, string? CredentialReference) : IRequest<SocialAccountDto>;
