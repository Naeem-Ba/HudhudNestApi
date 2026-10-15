using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Marketing.Commands.SetOfferStatus;

/// <summary>Allowed transitions: Draft→Active, Active→Paused, Paused→Active, and
/// Draft/Active/Paused→Ended (terminal). Repeating the current status is a no-op. Returns the
/// offer as it is now, or null when it does not exist.</summary>
public sealed record SetOfferStatusCommand(
    Guid Id,
    OfferStatus Status,
    Guid PerformedByUserId,
    string? IpAddress) : IRequest<OfferAdminDto?>;
