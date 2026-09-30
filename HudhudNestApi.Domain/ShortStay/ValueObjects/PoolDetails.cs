using HudhudNestApi.Domain.ShortStay.Enums;

namespace HudhudNestApi.Domain.ShortStay.ValueObjects;

/// <summary>Owned value object on ShortStayListing — null when the listing has no pool.</summary>
public sealed class PoolDetails
{
    public PoolType Type { get; private set; }
    public PoolLocation Location { get; private set; }
    public bool IsSeasonal { get; private set; }
    public bool IsHeated { get; private set; }

    private PoolDetails() { }

    public static PoolDetails Create(PoolType type, PoolLocation location, bool isSeasonal, bool isHeated)
        => new() { Type = type, Location = location, IsSeasonal = isSeasonal, IsHeated = isHeated };
}
