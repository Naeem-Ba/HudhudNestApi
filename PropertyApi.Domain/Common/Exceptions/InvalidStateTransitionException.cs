using System;

namespace PropertyApi.Domain.Common.Exceptions;

/// <summary>
/// A stricter <see cref="DomainException"/> for the specific case of an entity method being
/// called while the entity is in a status that does not allow it (e.g. accepting a service
/// request twice). Kept as its own type — rather than folded into the generic
/// <see cref="DomainException"/> → 400 mapping — because ExceptionHandlingMiddleware maps it
/// to 409 Conflict: "this action conflicts with the resource's current state" is a more
/// accurate HTTP status than "the request itself was malformed."
///
/// Domain stays free of any HTTP/Infrastructure concept by only naming the exception type;
/// the middleware (API layer) is what decides 409 belongs to it.
/// </summary>
public class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string message)
        : base(message)
    {
    }
}
