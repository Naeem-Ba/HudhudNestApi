using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.DispatchPropertyDistribution;

/// <summary>
/// Manual admin-triggered distribution run (spec §18: "POST /api/social-distribution/dispatch/{propertyId}",
/// spec §11's TriggerType.Manual/Republish). Runs the exact same DistributionEngine pass the
/// automatic PropertyPublished trigger uses — an admin re-running this for a property that was
/// already distributed once is an intentional republish, not a bug: DistributionEngine's
/// per-account duplicate guard only blocks a second publication while a live one still exists for
/// that (property, account) pair, never a fresh one after the previous run's publication was
/// cancelled or failed permanently.
/// </summary>
public sealed record DispatchPropertyDistributionCommand(Guid PropertyId, Guid TriggeredByUserId) : IRequest<DistributionRunDto>;
