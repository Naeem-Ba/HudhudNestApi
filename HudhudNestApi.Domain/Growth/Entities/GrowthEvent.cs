using HudhudNestApi.Domain.Common.Exceptions;

namespace HudhudNestApi.Domain.Growth.Entities;

/// <summary>
/// Growth engine P4: one row of the append-only growth event log (registrations, publications, demand,
/// contact). It feeds the growth dashboard and referral qualification.
///
/// Privacy: no name, e-mail, phone, IP address or free text. <see cref="UserId"/> is the internal account id.
/// Account deletion anonymizes the account row, so the id then points at no personal data; a hard delete of the
/// row sets it to null (counts survive).
/// Source/Medium/Campaign are short marketing tags taken from UTM parameters or a referral link.
///
/// Not a BaseEntity: rows are never updated or soft-deleted. <see cref="IdempotencyKey"/> is unique, so a
/// retried request or a second call for the same fact writes nothing.
/// </summary>
public sealed class GrowthEvent
{
    public const int NameMaxLength = 48;
    public const int KeyMaxLength = 160;
    public const int TagMaxLength = 64;

    public long Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTime OccurredAt { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? PropertyId { get; private set; }
    public Guid? SubjectId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? CorrelationId { get; private set; }
    public string? Source { get; private set; }
    public string? Medium { get; private set; }
    public string? Campaign { get; private set; }

    private GrowthEvent() { }

    public static GrowthEvent Create(
        string name,
        string idempotencyKey,
        DateTime occurredAtUtc,
        Guid? userId = null,
        Guid? propertyId = null,
        Guid? subjectId = null,
        string? correlationId = null,
        string? source = null,
        string? medium = null,
        string? campaign = null)
    {
        if (!GrowthEventNames.IsKnown(name))
            throw new DomainException($"Unknown growth event '{name}'.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > KeyMaxLength)
            throw new DomainException("A growth event needs an idempotency key of at most 160 characters.");

        return new GrowthEvent
        {
            Name = name,
            IdempotencyKey = idempotencyKey,
            OccurredAt = occurredAtUtc,
            UserId = userId,
            PropertyId = propertyId,
            SubjectId = subjectId,
            CorrelationId = Tag(correlationId),
            Source = Tag(source),
            Medium = Tag(medium),
            Campaign = Tag(campaign),
        };
    }

    /// <summary>
    /// Keeps a marketing tag short and plain: lower case, letters, digits, dash, underscore and dot only.
    /// Anything else (spaces, e-mail addresses, URLs) is dropped rather than stored.
    /// </summary>
    public static string? Tag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.Length > TagMaxLength)
            trimmed = trimmed[..TagMaxLength];
        foreach (var c in trimmed)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
                return null;
        }
        return trimmed;
    }
}

/// <summary>The stable event names. Renaming one breaks the dashboard history: add a new name instead.</summary>
public static class GrowthEventNames
{
    public const string UserRegistered = "user_registered";
    public const string ListingPublished = "listing_published";
    public const string SearchRequestCreated = "search_request_created";
    public const string SavedSearchCreated = "saved_search_created";
    public const string FavoriteAdded = "favorite_added";
    public const string ContactStarted = "contact_started";
    public const string ListingProposed = "listing_proposed";
    public const string ReferralQualified = "referral_qualified";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        UserRegistered, ListingPublished, SearchRequestCreated, SavedSearchCreated,
        FavoriteAdded, ContactStarted, ListingProposed, ReferralQualified,
    };

    public static bool IsKnown(string? name) => name is not null && All.Contains(name);
}
