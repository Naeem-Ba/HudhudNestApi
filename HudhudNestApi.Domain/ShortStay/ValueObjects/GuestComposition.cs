using HudhudNestApi.Domain.Common.Exceptions;

namespace HudhudNestApi.Domain.ShortStay.ValueObjects;

/// <summary>Owned value object — always persisted inline on Booking, never on its own table.</summary>
public sealed class GuestComposition
{
    public int Adults { get; private set; }
    public int Children { get; private set; }
    public int Infants { get; private set; }

    /// <summary>Guests counted against a listing/room-type's Capacity. Infants are excluded
    /// by convention (common industry practice — a crib doesn't occupy a "sleeping slot").</summary>
    public int CountedGuests => Adults + Children;

    private GuestComposition() { }

    public static GuestComposition Create(int adults, int children, int infants)
    {
        if (adults < 1)
            throw new DomainException("يجب أن يكون هناك بالغ واحد على الأقل في الحجز.");
        if (children < 0 || infants < 0)
            throw new DomainException("عدد الضيوف لا يمكن أن يكون سالباً.");

        return new GuestComposition { Adults = adults, Children = children, Infants = infants };
    }
}
