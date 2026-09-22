using MediatR;

namespace HudhudNestApi.Application.Listings.Commands.ConfirmPropertyAvailability;

/// <summary>
/// Phase-0, Task 2 — cheap "still available?" confirmation. Mirrors
/// PublishPropertyCommand's shape: a single-tap action, no request body needed.
/// </summary>
public sealed record ConfirmPropertyAvailabilityCommand(
    Guid PropertyId,
    Guid RequestingUserId,
    bool IsAdmin) : IRequest;
