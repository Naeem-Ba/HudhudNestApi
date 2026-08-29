using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.ShortStay.ValueObjects;

namespace PropertyApi.Application.Tests.ShortStay.Domain;

public sealed class GuestCompositionTests
{
    [Fact]
    public void Create_Valid_SetsFieldsAndCountedGuests()
    {
        var guests = GuestComposition.Create(adults: 2, children: 1, infants: 1);

        Assert.Equal(2, guests.Adults);
        Assert.Equal(1, guests.Children);
        Assert.Equal(1, guests.Infants);
        Assert.Equal(3, guests.CountedGuests); // infants excluded from capacity count
    }

    [Fact]
    public void Create_Throws_WhenNoAdults()
    {
        Assert.Throws<DomainException>(() => GuestComposition.Create(0, 1, 0));
    }

    [Theory]
    [InlineData(1, -1, 0)]
    [InlineData(1, 0, -1)]
    public void Create_Throws_WhenNegativeCounts(int adults, int children, int infants)
    {
        Assert.Throws<DomainException>(() => GuestComposition.Create(adults, children, infants));
    }
}
