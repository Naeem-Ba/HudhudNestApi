using MediatR;

namespace WohnungenApi.Application.Listings.Commands.DeleteProperty;

/// <summary>
/// Soft-deletes a property listing.
/// Returns true if found and deleted, false if not found.
/// Throws UnauthorizedAccessException if caller is not the owner.
///
/// BUG FIX: Was an empty "internal class DeletePropertyCommand {}" — completely missing.
/// </summary>
public sealed record DeletePropertyCommand(
    Guid PropertyId,
    Guid RequestingUserId
) : IRequest<bool>;