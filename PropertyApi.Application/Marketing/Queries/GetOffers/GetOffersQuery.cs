using MediatR;
using PropertyApi.Application.Marketing.DTOs;

namespace PropertyApi.Application.Marketing.Queries.GetOffers;

/// <summary>Admin listing — every offer regardless of status, unlike the public
/// GetActiveOfferQuery which returns at most one.</summary>
public sealed record GetOffersQuery : IRequest<IReadOnlyList<OfferAdminDto>>;
