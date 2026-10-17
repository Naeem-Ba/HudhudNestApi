namespace HudhudNestApi.Domain.Services.Enums;

/// <summary>The customer's reply to "the provider says this request is finished: is that right?".</summary>
public enum ServiceRequesterAnswer
{
    /// <summary>Agrees with the provider's outcome (service done, or truly not done).</summary>
    Confirmed = 1,

    /// <summary>Disagrees: the admin should look at this request.</summary>
    Disputed = 2,
}
