namespace HudhudNestApi.Domain.ShortStay.Enums;

/// <summary>OnlinePayment exists in the model for forward-compatibility but has no active
/// gateway wired up yet — bookings using it are rejected at the validator level until a
/// payment provider is integrated (see plan's "مؤجَّل صراحةً" section).</summary>
public enum PaymentMethod
{
    PayOnArrival = 0,
    PhoneConfirmation = 1,
    ChatConfirmation = 2,
    OnlinePayment = 3,
}
