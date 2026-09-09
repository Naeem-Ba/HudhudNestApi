using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.AiContent;

/// <summary>
/// Structured output of <see cref="ISocialContentGenerator"/> (Phase 8 spec §3: "يجب أن يعيد AI
/// بنية منظمة، وليس نصًا حرًا فقط"). <see cref="SourceFactsHash"/> lets
/// <see cref="SocialContentFactValidator"/>/callers detect that the facts changed between
/// generation and use without re-comparing every field by hand. <see cref="RequiresReview"/>
/// lets a generator flag its own output as unsafe to auto-queue (spec: "Human Review عند وجود
/// تعارض... أو محتوى حساس") — today's <see cref="TemplateSocialContentGenerator"/> never sets it,
/// since it never says anything beyond the supplied facts.
/// </summary>
public sealed record GeneratedSocialContent(
    SocialPlatform Platform,
    string? Title,
    string Body,
    string? Caption,
    IReadOnlyList<string> Hashtags,
    string? CallToAction,
    string Language,
    string SourceFactsHash,
    bool RequiresReview);
