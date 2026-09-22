using HudhudNestApi.Application.ShortStay.Queries.GetPricingPreview;

namespace HudhudNestApi.Application.Tests.ShortStay;

public sealed class GetPricingPreviewQueryValidatorTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    private static GetPricingPreviewQuery Query(
        Guid? unitId = null, int checkInOffset = 1, int nights = 2, int adults = 2, int children = 0, int infants = 0) =>
        new(unitId ?? Guid.NewGuid(), Today.AddDays(checkInOffset), Today.AddDays(checkInOffset + nights), adults, children, infants);

    private static bool IsValid(GetPricingPreviewQuery query) => new GetPricingPreviewQueryValidator().Validate(query).IsValid;

    [Fact]
    public void Valid_Query_Passes() => Assert.True(IsValid(Query()));

    [Fact]
    public void Today_IsAllowedAsCheckIn() => Assert.True(IsValid(Query(checkInOffset: 0)));

    [Fact]
    public void EmptyUnitId_Fails() => Assert.False(IsValid(Query(unitId: Guid.Empty)));

    [Fact]
    public void PastCheckIn_Fails() => Assert.False(IsValid(Query(checkInOffset: -1)));

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void CheckOut_MustBeAfterCheckIn(int nights) => Assert.False(IsValid(Query(nights: nights)));

    [Fact]
    public void AtLeastOneAdult_IsRequired() => Assert.False(IsValid(Query(adults: 0)));

    [Fact]
    public void NegativeChildrenOrInfants_Fail()
    {
        Assert.False(IsValid(Query(children: -1)));
        Assert.False(IsValid(Query(infants: -1)));
    }
}
