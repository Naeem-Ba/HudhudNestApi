using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.RetrySocialPublication;

/// <summary>Manual retry from Failed (spec §12) — moves back to Queued; actual re-attempt happens the same way any Queued publication would (worker sweep, or an explicit publish-now call).</summary>
public sealed record RetrySocialPublicationCommand(Guid PublicationId, Guid ActorUserId) : IRequest<SocialPublicationDto>;
