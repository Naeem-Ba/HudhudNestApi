namespace PropertyApi.Domain.ShortStay.Enums;

/// <summary>
/// Controls how much of a listing's location is exposed to an unauthorized viewer.
/// Approximate must be enforced server-side in DTO mapping — never sent to the client
/// and redacted in the UI, since that can be bypassed by calling the API directly.
/// </summary>
public enum LocationVisibility
{
    Approximate = 0,
    Exact = 1,
}
