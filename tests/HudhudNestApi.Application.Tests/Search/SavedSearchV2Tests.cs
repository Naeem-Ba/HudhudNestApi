using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Search.Entities;
using HudhudNestApi.Domain.Search.Enums;
using Xunit;

namespace HudhudNestApi.Application.Tests.Search;

/// <summary>Growth engine P3: saved-search state rules.</summary>
public sealed class SavedSearchV2Tests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SavedSearch Make(SavedSearchFrequency frequency = SavedSearchFrequency.Instant) =>
        SavedSearch.Create(Guid.NewGuid(), "x", null, "Damascus", null, null, null, null, null, ListingType.ForRent,
            null, null, null, null, null, null, null, hasBalcony: false, hasElevator: true, frequency: frequency);

    [Fact]
    public void Amenity_filters_keep_only_true()
    {
        var s = Make();
        Assert.Null(s.HasBalcony);
        Assert.True(s.HasElevator);
        Assert.Null(s.HasParkingSpace);
    }

    [Fact]
    public void Defaults_are_instant_and_active()
    {
        var s = SavedSearch.Create(Guid.NewGuid(), null, null, "x", null, null, null, null, null, null, null, null, null, null, null, null, null);
        Assert.Equal(SavedSearchFrequency.Instant, s.Frequency);
        Assert.False(s.IsPaused);
        Assert.Equal(0, s.NotifiedCount);
        Assert.True(s.IsDigestDue(Now));
    }

    [Fact]
    public void Update_moves_the_cursor_to_now_so_old_listings_are_not_announced()
    {
        var s = Make();
        s.Update("new", null, "Homs", null, null, null, null, null, ListingType.ForSale, 1, 2, "USD", 1, 3, 50, 90,
            true, null, true, SavedSearchFrequency.Daily, Now);
        Assert.Equal(Now, s.LastMatchedAt);
        Assert.Equal("Homs", s.City);
        Assert.Equal(SavedSearchFrequency.Daily, s.Frequency);
        Assert.True(s.HasBalcony);
        Assert.Null(s.HasElevator);
        Assert.True(s.HasParkingSpace);
    }

    [Fact]
    public void Resume_starts_from_now_and_is_a_no_op_when_active()
    {
        var s = Make();
        s.Resume(Now);
        Assert.Null(s.LastMatchedAt);

        s.Pause(Now);
        Assert.True(s.IsPaused);
        s.Resume(Now.AddDays(2));
        Assert.False(s.IsPaused);
        Assert.Equal(Now.AddDays(2), s.LastMatchedAt);
    }

    [Fact]
    public void Daily_is_due_again_after_24_hours()
    {
        var s = Make(SavedSearchFrequency.Daily);
        Assert.True(s.IsDigestDue(Now));
        s.RecordDelivery(4, Now);
        Assert.False(s.IsDigestDue(Now.AddHours(23)));
        Assert.True(s.IsDigestDue(Now.AddHours(24)));
        Assert.Equal(4, s.NotifiedCount);
    }

    [Fact]
    public void Instant_is_always_due_and_empty_deliveries_are_not_logged()
    {
        var s = Make();
        s.RecordDelivery(0, Now);
        Assert.Null(s.LastNotifiedAt);
        s.RecordDelivery(2, Now);
        Assert.True(s.IsDigestDue(Now.AddMinutes(1)));
        Assert.Equal(2, s.NotifiedCount);
    }
}
