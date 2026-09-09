namespace PropertyApi.Domain.Listings.Enums;

/// <summary>
/// The distribution channel a visitor used to share a property listing (Social Sharing &amp;
/// Distribution feature). Kept as a closed enum — not a free-text string — so
/// <see cref="Entities.PropertyShareEvent"/> can never record a platform the frontend/backend
/// don't both know about; the controller validates the incoming value against this enum before
/// anything is persisted (never trust a client-supplied string here).
/// </summary>
public enum SharePlatform
{
    Facebook = 1,
    WhatsApp = 2,
    Telegram = 3,

    /// <summary>Device-native share sheet (Web Share API, or a future Capacitor Share plugin).</summary>
    Native = 4,

    CopyLink = 5,

    /// <summary>
    /// Anything else, including the Instagram entry point (which itself only ever delegates to
    /// the native share sheet or copy-link — there is no official Instagram web publishing API,
    /// see PropertyShareButtonComponent on the frontend).
    /// </summary>
    Other = 6
}
