namespace HudhudNestApi.Application.SocialDistribution.AiContent;

/// <summary>
/// The ONLY source of truth an <see cref="ISocialContentGenerator"/> may draw property facts
/// from (Phase 8 spec §3: "البيانات الحساسة والمهمة يجب أن تأتي مباشرة من Domain أو DTO موثوق").
/// Built exclusively from <c>Property</c> (Listings bounded context) by the caller
/// (<c>CreateSocialPublicationCommandHandler</c>) — a generator never loads a Property itself and
/// never receives anything wider than this record, so it structurally cannot invent a field that
/// isn't here and cannot see unrelated Property/owner data it has no business seeing.
/// </summary>
/// <param name="CanonicalUrl">
/// The name mirrors the spec's TypeScript interface, but the value actually supplied is the
/// UTM-attributed distribution TargetUrl (Phase 2), not the property page's plain
/// <c>&lt;link rel="canonical"&gt;</c> — social copy is meant to carry the platform's UTM
/// parameters (the entire point of Phase 2 attribution), unlike the page's own canonical/og:url
/// metadata, which must stay UTM-free.
/// </param>
public sealed record PropertySocialFacts(
    Guid PropertyId,
    string Title,
    string? PropertyType,
    string TransactionType,
    string? Province,
    string? City,
    string? Address,
    decimal? Price,
    string? Currency,
    decimal? Area,
    int? Rooms,
    int? Bathrooms,
    string Status,
    string CanonicalUrl);
