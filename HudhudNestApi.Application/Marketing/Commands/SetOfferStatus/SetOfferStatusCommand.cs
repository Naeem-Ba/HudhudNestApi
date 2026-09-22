using MediatR;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Marketing.Commands.SetOfferStatus;

public sealed record SetOfferStatusCommand(Guid Id, OfferStatus Status) : IRequest<bool>;
