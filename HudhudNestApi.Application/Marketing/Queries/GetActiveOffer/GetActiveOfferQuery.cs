using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;

namespace HudhudNestApi.Application.Marketing.Queries.GetActiveOffer;

/// <summary>Null result means "no offer to show" — the frontend must hide the section
/// entirely rather than render a disabled/expired-looking card.</summary>
public sealed record GetActiveOfferQuery : IRequest<OfferDto?>;
