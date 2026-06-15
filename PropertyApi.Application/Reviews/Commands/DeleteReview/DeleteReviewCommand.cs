using MediatR;

namespace PropertyApi.Application.Reviews.Commands.DeleteReview;

/// <summary>A user deletes their own review (or an admin deletes any review).</summary>
public sealed record DeleteReviewCommand(
    Guid ReviewId,
    Guid ActorId,
    bool IsAdmin = false
) : IRequest<bool>;