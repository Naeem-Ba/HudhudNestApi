using MediatR;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Marketing.Commands.UpdateOffer;

public sealed record UpdateOfferCommand(
    Guid Id,
    string Name,
    OfferDiscountType DiscountType,
    decimal DiscountValue,
    DateTime StartsAtUtc,
    string? Description,
    string? TargetPlanTier,
    DateTime? EndsAtUtc,
    int? MaxRedemptions,
    string? Terms) : IRequest<bool>;
