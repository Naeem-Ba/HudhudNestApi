using MediatR;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Application.Marketing.Commands.SetOfferStatus;

public sealed record SetOfferStatusCommand(Guid Id, OfferStatus Status) : IRequest<bool>;
