using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Auth.Tests.Domain.Services;

[Trait("Category", "Domain")]
[Trait("Entity", "ServiceReview")]
public sealed class ServiceReviewModerationTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);

    private static ServiceReview CreateReview() =>
        ServiceReview.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4, "جيد", UtcNow);

    [Fact(DisplayName = "Hide: marks the review hidden and stores the reason and time")]
    public void Hide_StoresReason()
    {
        var review = CreateReview();

        review.Hide("  إساءة  ", UtcNow.AddHours(1));

        Assert.True(review.IsHidden);
        Assert.Equal("إساءة", review.HiddenReason);
        Assert.Equal(UtcNow.AddHours(1), review.HiddenAt);
    }

    [Fact(DisplayName = "Hide: a blank reason throws DomainException")]
    public void Hide_BlankReason_Throws()
    {
        var review = CreateReview();

        Assert.Throws<DomainException>(() => review.Hide(" ", UtcNow));
        Assert.False(review.IsHidden);
    }

    [Fact(DisplayName = "Unhide: clears the hidden flag, reason and time")]
    public void Unhide_ClearsFlags()
    {
        var review = CreateReview();
        review.Hide("خطأ", UtcNow);

        review.Unhide(UtcNow.AddMinutes(5));

        Assert.False(review.IsHidden);
        Assert.Null(review.HiddenReason);
        Assert.Null(review.HiddenAt);
    }
}
