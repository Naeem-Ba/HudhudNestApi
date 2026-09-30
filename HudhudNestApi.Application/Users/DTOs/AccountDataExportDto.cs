namespace HudhudNestApi.Application.Users.DTOs;

/// <summary>
/// Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md): everything a self-service data
/// export returns. Assembled explicitly, field by field, by
/// <c>ExportMyDataQueryHandler</c>/<c>IAccountDataExportRepository</c> -- deliberately NOT a
/// generic serializer over the entity graph, so a secret field can never end up here by
/// omission. Excludes: PasswordHash, SecurityStamp, RefreshTokens, PhoneNumberLookupHash, any
/// other user's data, DataProtectionKeys.
/// </summary>
public sealed class AccountDataExportDto
{
    public DateTime ExportedAtUtc { get; init; }

    public AccountDataExportProfile Account { get; init; } = new();

    public IReadOnlyList<AccountDataExportProperty> Properties { get; init; } = Array.Empty<AccountDataExportProperty>();

    public IReadOnlyList<AccountDataExportReview> ReviewsWritten { get; init; } = Array.Empty<AccountDataExportReview>();

    public IReadOnlyList<AccountDataExportFavorite> Favorites { get; init; } = Array.Empty<AccountDataExportFavorite>();

    public IReadOnlyList<AccountDataExportVisitRequest> VisitRequestsMade { get; init; } = Array.Empty<AccountDataExportVisitRequest>();

    public IReadOnlyList<AccountDataExportConsent> Consents { get; init; } = Array.Empty<AccountDataExportConsent>();

    public IReadOnlyList<AccountDataExportUserRating> RatingsGiven { get; init; } = Array.Empty<AccountDataExportUserRating>();

    public IReadOnlyList<AccountDataExportUserRating> RatingsReceived { get; init; } = Array.Empty<AccountDataExportUserRating>();
}

public sealed class AccountDataExportProfile
{
    public Guid Id { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Bio { get; init; }
    public string? ContactInfo { get; init; }
    public string PreferredLanguage { get; init; } = string.Empty;
    public string PreferredCurrency { get; init; } = string.Empty;
    public string? CountryCode { get; init; }
    public string? PlanTier { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public DateTime CreatedAt { get; init; }
}

public sealed class AccountDataExportProperty
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PublishedAt { get; init; }
}

public sealed class AccountDataExportReview
{
    public Guid PropertyId { get; init; }
    public string PropertyTitle { get; init; } = string.Empty;
    public int Rating { get; init; }
    public string? Comment { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class AccountDataExportFavorite
{
    public Guid PropertyId { get; init; }
    public string PropertyTitle { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public sealed class AccountDataExportVisitRequest
{
    public Guid PropertyId { get; init; }
    public string PropertyTitle { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime ProposedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class AccountDataExportConsent
{
    public string PolicyType { get; init; } = string.Empty;
    public string PolicyVersion { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public DateTime ConsentedAtUtc { get; init; }
    public DateTime? WithdrawnAtUtc { get; init; }
}

public sealed class AccountDataExportUserRating
{
    public Guid CounterpartyUserId { get; init; }
    public int Credibility { get; init; }
    public int Safety { get; init; }
    public int ResponseSpeed { get; init; }
    public int Transparency { get; init; }
    public double OverallScore { get; init; }
    public string? Comment { get; init; }
    public DateTime CreatedAt { get; init; }
}
