using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.PublishSocialPublication;

/// <summary>
/// Executes one publish attempt for a Queued/Retrying publication. The ONE code path both the
/// "publish now" admin endpoint and SocialPublicationDispatchHostedService call — there is no
/// separate "real" worker-only implementation, so manual and scheduled publishing can never
/// drift apart.
/// </summary>
public sealed record PublishSocialPublicationCommand(Guid PublicationId) : IRequest<SocialPublicationDto>;
