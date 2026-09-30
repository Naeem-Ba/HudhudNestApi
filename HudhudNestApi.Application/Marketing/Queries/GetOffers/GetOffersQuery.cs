using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;

namespace HudhudNestApi.Application.Marketing.Queries.GetOffers;

/// <summary>Admin listing — every offer regardless of status, unlike the public
/// GetActiveOfferQuery which returns at most one.</summary>
public sealed record GetOffersQuery : IRequest<IReadOnlyList<OfferAdminDto>>;
