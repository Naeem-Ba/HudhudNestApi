using MediatR;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Marketing.Commands.CreateOffer;

/// <summary>Admin-only. A new offer always starts as Draft — see SetOfferStatusCommand to
/// go live, a deliberate two-step so an admin can review before it's public.</summary>
public sealed record CreateOfferCommand(
    string Name,
    OfferDiscountType DiscountType,
    decimal DiscountValue,
    DateTime StartsAtUtc,
    string? Description,
    string? TargetPlanTier,
    DateTime? EndsAtUtc,
    int? MaxRedemptions,
    string? Terms) : IRequest<Guid>;
