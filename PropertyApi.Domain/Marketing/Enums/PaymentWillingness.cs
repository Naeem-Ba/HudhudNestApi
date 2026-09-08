namespace PropertyApi.Domain.Marketing.Enums;

/// <summary>Answer to the survey's core question: "هل أنت مستعد للدفع مقابل استخدام هذه
/// الخدمة؟". A tri-state, not a bool — "Maybe" is a real, common, informative answer for a
/// pre-launch pricing survey and collapsing it into yes/no would throw away signal.</summary>
public enum PaymentWillingness
{
    Yes = 1,
    No = 2,
    Maybe = 3
}
