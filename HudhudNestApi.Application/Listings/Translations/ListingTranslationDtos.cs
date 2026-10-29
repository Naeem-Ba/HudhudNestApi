namespace HudhudNestApi.Application.Listings.Translations;

/// <summary>Per-field outcome. <c>Text</c> is set only for "ready".</summary>
public sealed class ListingTranslationFieldDto
{
    /// <summary>ready | pending | same_language | unavailable | empty</summary>
    public string State { get; set; } = "empty";
    public string? SourceLanguage { get; set; }
    public string? Text { get; set; }

    /// <summary>For "unavailable": disabled | quota | provider_error | unknown_source_language | too_long | cooldown | source_changed</summary>
    public string? Reason { get; set; }
}

public sealed class ListingTranslationDto
{
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>ready | pending | same_language | unavailable - the client shows the original text for anything but "ready".</summary>
    public string Status { get; set; } = "unavailable";
    public string? Reason { get; set; }
    public ListingTranslationFieldDto Title { get; set; } = new();
    public ListingTranslationFieldDto Description { get; set; } = new();

    /// <summary>When "pending": how long to wait before asking again.</summary>
    public int? RetryAfterSeconds { get; set; }
}
