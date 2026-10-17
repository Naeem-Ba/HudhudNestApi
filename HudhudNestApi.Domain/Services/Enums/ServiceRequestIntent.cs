namespace HudhudNestApi.Domain.Services.Enums;

/// <summary>
/// Which button the customer pressed to open the conversation. Both start as an inquiry; the intent
/// is kept for the statistics ("asked a question" vs "wanted to order").
/// </summary>
public enum ServiceRequestIntent
{
    /// <summary>"Ask more": the customer wants information first.</summary>
    Inquire = 1,

    /// <summary>"Request this service": the customer wants to order, pending their own confirmation.</summary>
    Request = 2,
}
