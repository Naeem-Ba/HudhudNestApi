namespace HudhudNestApi.Application.SocialDistribution.AiContent;

/// <summary>
/// Port for turning verified property facts into platform-tailored social copy (Phase 8 spec
/// §3). "AI" in the phase name is aspirational, not mandatory — the only implementation shipped
/// today (<see cref="TemplateSocialContentGenerator"/>) is a deterministic, rule-based writer
/// with zero external calls and zero API keys, chosen deliberately: this environment has no LLM
/// provider/API key configured, and inventing one (or a fake HTTP call to a "content AI") would
/// violate the platform's own rule against fabricating externally-verifiable functionality.
///
/// A future <c>AiSocialContentGenerator</c> (backed by a real LLM) plugs in behind this exact
/// interface with zero changes to <see cref="Commands.CreateSocialPublication.CreateSocialPublicationCommandHandler"/>,
/// <c>DistributionEngine</c>, the Queue, or any Publisher — it would only need its own
/// configuration section (endpoint + API key, supplied by whoever operates it) and would still be
/// required to run its output through <see cref="SocialContentFactValidator"/> before anything
/// downstream ever sees it, exactly like this implementation is.
/// </summary>
public interface ISocialContentGenerator
{
    Task<GeneratedSocialContent> GenerateAsync(GenerateSocialContentRequest request, CancellationToken ct = default);
}
