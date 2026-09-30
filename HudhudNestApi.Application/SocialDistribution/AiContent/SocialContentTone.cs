namespace HudhudNestApi.Application.SocialDistribution.AiContent;

/// <summary>Optional stylistic hint for a content generator (Phase 8 spec §3). Purely presentational — never affects which facts are allowed to appear.</summary>
public enum SocialContentTone
{
    Neutral = 0,
    Professional = 1,
    Friendly = 2,
    Urgent = 3,
}
