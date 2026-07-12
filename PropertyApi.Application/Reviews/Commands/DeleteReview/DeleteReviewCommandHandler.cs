using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Reviews.Interfaces;
using PropertyApi.Application.Common.Exceptions;

namespace PropertyApi.Application.Reviews.Commands.DeleteReview;

public sealed class DeleteReviewCommandHandler
    : IRequestHandler<DeleteReviewCommand, bool>
{
    private readonly IPropertyReviewRepository _reviews;
    private readonly IUnitOfWork _uow;

    public DeleteReviewCommandHandler(IPropertyReviewRepository reviews, IUnitOfWork uow)
        => (_reviews, _uow) = (reviews, uow);

    public async Task<bool> Handle(DeleteReviewCommand request, CancellationToken ct)
    {
        var review = await _reviews.GetByIdAsync(request.ReviewId, ct)
            ?? throw new NotFoundException("Ø§Ù„ØªÙ‚ÙŠÙŠÙ… ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯.");

        if (!request.IsAdmin && review.ReviewerId != request.ActorId)
            throw new ForbiddenException("ÙŠÙ…ÙƒÙ†Ùƒ Ø­Ø°Ù ØªÙ‚ÙŠÙŠÙ…Ø§ØªÙƒ ÙÙ‚Ø·.");

        await _reviews.DeleteAsync(review, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
