namespace HudhudNestApi.Domain.Listings;

/// <summary>
/// Growth engine P8: approximate coordinates for listings whose owner hid the exact spot. The point is snapped
/// to the centre of a fixed grid cell of <see cref="CellDegrees"/> (about 550 m north-south in Syria), so the
/// same listing always shows the same point and repeated requests reveal nothing more (unlike random jitter,
/// which averages out).
/// </summary>
public static class LocationPrivacy
{
    public const decimal CellDegrees = 0.005m;

    public static decimal Approximate(decimal coordinate) =>
        decimal.Round(decimal.Floor(coordinate / CellDegrees) * CellDegrees + CellDegrees / 2m, 6);

    public static decimal? Approximate(decimal? coordinate) => coordinate is { } c ? Approximate(c) : null;
}
