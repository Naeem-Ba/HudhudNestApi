using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Domain.Reviews.Entities;

/// <summary>
/// A user review on a property.
/// Business rule: one review per user per property (enforced by unique DB constraint).
/// </summary>
public sealed class PropertyReview : BaseEntity
{
    public Guid PropertyId { get; private set; }
    public Guid ReviewerId { get; private set; }
    public int Rating { get; private set; }   // 1..5
    public string? Comment { get; private set; }

    // EF navigation
    public Property? Property { get; private set; }
    public UserAccount? Reviewer { get; private set; }

    private PropertyReview() { }

    public static PropertyReview Create(
        Guid propertyId, Guid reviewerId,
        int rating, string? comment = null)
    {
        if (rating is < 1 or > 5)
            throw new DomainException("التقييم يجب أن يكون بين 1 و5.");

        return new PropertyReview
        {
            PropertyId = propertyId,
            ReviewerId = reviewerId,
            Rating = rating,
            Comment = comment?.Trim(),
        };
    }
}
