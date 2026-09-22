using MediatR;

namespace HudhudNestApi.Application.Listings.Commands.DeleteProperty;

/// <summary>
/// Soft-deletes a property listing.
/// Ownership is enforced in the Application service layer.
/// </summary>
public sealed record DeletePropertyCommand(
    Guid PropertyId,
    Guid RequestingUserId,
    string? IpAddress = null
) : IRequest<bool>;
