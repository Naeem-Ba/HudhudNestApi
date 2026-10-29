using System.Diagnostics;
using HudhudNestApi.Domain.Listings.Translations;
using HudhudNestApi.Domain.Users.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HudhudNestApi.Application.Listings.Translations;

public interface IListingTranslationService
{
    /// <summary>
    /// Returns the listing's title/description in <paramref name="targetLanguage"/> ("ar", "de", "en"), creating
    /// the translation if it is missing. Null when the listing does not exist or is not publicly viewable.
    /// Never throws for provider or database trouble in the translation path: the client falls back to the original.
    /// </summary>
    Task<ListingTranslationDto?> EnsureAsync(Guid propertyId, string targetLanguage, CancellationToken ct);
}

public sealed class ListingTranslationService : IListingTranslationService
{
    private readonly IListingTranslationStore _store;
    private readonly ITranslationProvider _provider;
    private readonly IOptionsMonitor<ListingTranslationOptions> _options;
    private readonly ILogger<ListingTranslationService> _logger;
    private readonly TimeProvider _time;

    public ListingTranslationService(
        IListingTranslationStore store,
        ITranslationProvider provider,
        IOptionsMonitor<ListingTranslationOptions> options,
        ILogger<ListingTranslationService> logger,
        TimeProvider? time = null)
    {
        _store = store;
        _provider = provider;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    private sealed class Work
    {
        public ListingTranslationField Field;
        public string Text = string.Empty;
        public string Hash = string.Empty;
        public string? SourceHint;          // "ar" | "de" | "en" | null (Latin, provider decides)
        public ListingTranslationFieldDto Result = new();
        public TranslationClaim? Claim;
    }

    public async Task<ListingTranslationDto?> EnsureAsync(Guid propertyId, string targetLanguage, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var target = SupportedPreferences.NormalizeLanguage(targetLanguage)
            ?? throw new ArgumentException("Unsupported language.", nameof(targetLanguage));
        var dto = new ListingTranslationDto { TargetLanguage = target };

        if (!options.Enabled)
        {
            dto.Status = "unavailable";
            dto.Reason = "disabled";
            ListingTranslationTelemetry.Outcome("disabled", target);
            return dto;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var source = await _store.GetPubliclyViewableTextAsync(propertyId, now, ct);
        if (source is null) return null;

        var stored = await _store.GetAsync(propertyId, target, ct);
        var pendingWork = new List<Work>();

        Resolve(ListingTranslationField.Title, source.Title, dto.Title);
        Resolve(ListingTranslationField.Description, source.Description, dto.Description);

        if (pendingWork.Count > 0)
        {
            if (!options.CreationEnabled || !_provider.IsAvailable)
            {
                foreach (var w in pendingWork) Unavailable(w.Result, "disabled", target);
            }
            else
            {
                await TranslateAsync(propertyId, target, pendingWork, options, ct);
            }
        }

        Summarise(dto);
        return dto;

        void Resolve(ListingTranslationField field, string text, ListingTranslationFieldDto result)
        {
            if (string.IsNullOrWhiteSpace(text)) { result.State = "empty"; return; }

            var detected = ListingLanguageDetector.Detect(text);
            if (detected is null) { Unavailable(result, "unknown_source_language", target, "unknown_language"); return; }
            if (detected == target)
            {
                result.State = "same_language";
                result.SourceLanguage = target;
                ListingTranslationTelemetry.Outcome("same_language", target);
                return;
            }
            if (text.Length > options.MaxFieldCharacters) { Unavailable(result, "too_long", target); return; }

            var hash = ListingTextFingerprint.Compute(text);
            var row = stored.FirstOrDefault(r => r.Field == field);
            if (row is not null && row.SourceHash == hash && row.PolicyVersion == options.PolicyVersion)
            {
                switch (row.Status)
                {
                    case ListingTranslationStatus.Completed when !string.IsNullOrWhiteSpace(row.TranslatedText):
                        result.State = "ready";
                        result.Text = row.TranslatedText;
                        result.SourceLanguage = row.SourceLanguage;
                        ListingTranslationTelemetry.Outcome("reused", target);
                        return;
                    case ListingTranslationStatus.SameLanguage:
                        result.State = "same_language";
                        result.SourceLanguage = row.SourceLanguage;
                        ListingTranslationTelemetry.Outcome("same_language", target);
                        return;
                    case ListingTranslationStatus.Pending when row.ClaimedUntil > now:
                        result.State = "pending";
                        ListingTranslationTelemetry.Outcome("pending_elsewhere", target);
                        return;
                    case ListingTranslationStatus.Failed when row.RetryAfter > now:
                        Unavailable(result, row.LastError == "quota" ? "quota" : "cooldown", target, "cooldown");
                        return;
                }
            }

            pendingWork.Add(new Work
            {
                Field = field,
                Text = text,
                Hash = hash,
                SourceHint = detected == ListingLanguageDetector.Latin ? null : detected,
                Result = result
            });
        }
    }

    private async Task TranslateAsync(
        Guid propertyId, string target, List<Work> work, ListingTranslationOptions options, CancellationToken ct)
    {
        var lease = TimeSpan.FromSeconds(options.ClaimLeaseSeconds);

        // Each group shares a source-language hint, so one provider call serves it (title and description are normally one group).
        foreach (var group in work.GroupBy(w => w.SourceHint))
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var members = group.ToList();
            var claims = await _store.TryClaimAsync(
                propertyId, target,
                members.Select(w => new TranslationClaimRequest(w.Field, w.SourceHint, w.Hash, w.Text.Length)).ToList(),
                options.PolicyVersion, _provider.Name, lease, now, ct);

            var claimed = new List<Work>();
            for (var i = 0; i < members.Count; i++)
            {
                members[i].Claim = claims[i];
                if (claims[i] is not null) claimed.Add(members[i]);
            }

            // Not claimed: another request owns it, it just finished, it is cooling down, or the listing changed/vanished.
            if (claimed.Count < members.Count)
            {
                var latest = await _store.GetAsync(propertyId, target, ct);
                foreach (var w in members.Where(g => g.Claim is null))
                    ApplyLatest(w, latest.FirstOrDefault(r => r.Field == w.Field), target, options);
            }

            if (claimed.Count == 0) continue;

            var characters = claimed.Sum(w => (long)w.Text.Length);
            var month = _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM");
            var reserved = await _store.TryReserveBudgetAsync(month, characters, options.MonthlyCharacterLimit, now, ct);
            if (!reserved)
            {
                var retry = now.AddMinutes(options.QuotaCooldownMinutes);
                foreach (var w in claimed)
                {
                    await _store.FailAsync(w.Claim!, "quota", countAttempt: false, retry, now, CancellationToken.None);
                    Unavailable(w.Result, "quota", target);
                }
                continue;
            }

            ListingTranslationTelemetry.Characters(_provider.Name, characters);
            var (batch, outcome) = await CallProviderAsync(claimed, group.Key, target, options);

            foreach (var (w, index) in claimed.Select((w, i) => (w, i)))
            {
                var finishedAt = _time.GetUtcNow().UtcDateTime;
                if (batch.Items is not null && TryValidate(w, batch.Items[index], target, out var text, out var sourceLanguage, out var sameLanguage))
                {
                    var stored = await _store.CompleteAsync(
                        w.Claim!, w.Field,
                        sameLanguage ? ListingTranslationStatus.SameLanguage : ListingTranslationStatus.Completed,
                        sameLanguage ? null : text, sourceLanguage, finishedAt, CancellationToken.None);

                    if (!stored)
                    {
                        // Edited or deleted while translating (or the claim was taken over): nothing was stored.
                        w.Result.State = "pending";
                        ListingTranslationTelemetry.Outcome("source_changed", target);
                        _logger.LogInformation(
                            "Listing translation discarded: source changed or listing removed while translating. Listing {PropertyId}, field {Field}, target {Target}.",
                            propertyId, w.Field, target);
                    }
                    else if (sameLanguage)
                    {
                        w.Result.State = "same_language";
                        w.Result.SourceLanguage = sourceLanguage;
                        ListingTranslationTelemetry.Outcome("same_language", target);
                    }
                    else
                    {
                        w.Result.State = "ready";
                        w.Result.Text = text;
                        w.Result.SourceLanguage = sourceLanguage;
                        ListingTranslationTelemetry.Outcome("created", target);
                    }
                    continue;
                }

                var kind = batch.Failure ?? TranslationFailureKind.Permanent;
                var attempts = w.Claim!.AttemptsBefore + 1;
                var cooldown = kind is TranslationFailureKind.Transient or TranslationFailureKind.Timeout && attempts < options.MaxAttempts
                    ? TimeSpan.FromSeconds(options.ShortCooldownSeconds * attempts)
                    : kind == TranslationFailureKind.QuotaExceeded
                        ? TimeSpan.FromMinutes(options.QuotaCooldownMinutes)
                        : TimeSpan.FromMinutes(options.LongCooldownMinutes);
                var code = kind switch
                {
                    TranslationFailureKind.Timeout => "timeout",
                    TranslationFailureKind.QuotaExceeded => "quota",
                    _ => batch.Items is not null ? "invalid_output" : "provider_error"
                };

                await _store.FailAsync(w.Claim!, code, countAttempt: true, finishedAt.Add(cooldown), finishedAt, CancellationToken.None);
                Unavailable(w.Result, code == "quota" ? "quota" : "provider_error", target, code == "timeout" ? "timeout" : code == "quota" ? "quota" : "failed");
            }

            _logger.LogInformation(
                "Listing translation batch finished. Listing {PropertyId}, target {Target}, fields {Fields}, characters {Characters}, outcome {Outcome}.",
                propertyId, target, claimed.Count, characters, outcome);
        }
    }

    private async Task<(TranslationBatchResult Result, string Outcome)> CallProviderAsync(
        List<Work> claimed, string? sourceHint, string target, ListingTranslationOptions options)
    {
        var started = Stopwatch.GetTimestamp();
        // The request's own token is deliberately not used: if the visitor navigates away the translation still
        // finishes and is stored for the next person. The provider timeout is what bounds the work.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.ProviderTimeoutSeconds));
        TranslationBatchResult result;
        try
        {
            result = await _provider.TranslateAsync(claimed.Select(w => w.Text).ToList(), sourceHint, target, cts.Token);
            if (result.Items is not null && result.Items.Count != claimed.Count)
                result = TranslationBatchResult.Fail(TranslationFailureKind.Permanent);
        }
        catch (OperationCanceledException)
        {
            result = TranslationBatchResult.Fail(TranslationFailureKind.Timeout);
        }
        catch (Exception ex)
        {
            // Type only: exception messages from HTTP stacks can embed URLs; never the texts or keys.
            _logger.LogWarning("Translation provider call threw {ExceptionType}.", ex.GetType().Name);
            result = TranslationBatchResult.Fail(TranslationFailureKind.Transient);
        }

        var outcome = result.Failure is null ? "ok" : result.Failure.Value.ToString().ToLowerInvariant();
        ListingTranslationTelemetry.Duration(_provider.Name, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        if (result.Failure == TranslationFailureKind.Timeout) ListingTranslationTelemetry.Outcome("timeout", target);
        else if (result.Failure is not null) ListingTranslationTelemetry.Outcome("failed", target);
        return (result, outcome);
    }

    /// <summary>Never trust provider output: it must be non-blank, not absurdly longer than the source, and free of control characters.</summary>
    private static bool TryValidate(
        Work w, TranslatedText item, string target, out string? text, out string? sourceLanguage, out bool sameLanguage)
    {
        text = null;
        sameLanguage = false;
        sourceLanguage = SupportedPreferences.NormalizeLanguage(item.DetectedSourceLanguage) ?? item.DetectedSourceLanguage?.ToLowerInvariant() ?? w.SourceHint;

        if (sourceLanguage == target)
        {
            sameLanguage = true;
            return true;
        }

        var candidate = item.Text?.Trim();
        if (string.IsNullOrEmpty(candidate)) return false;
        if (candidate.Length > w.Text.Length * 4 + 100) return false;
        if (candidate.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')) return false;

        text = candidate;
        return true;
    }

    private void ApplyLatest(Work w, StoredListingTranslation? row, string target, ListingTranslationOptions options)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        if (row is not null && row.SourceHash == w.Hash && row.PolicyVersion == options.PolicyVersion)
        {
            if (row.Status == ListingTranslationStatus.Completed && !string.IsNullOrWhiteSpace(row.TranslatedText))
            {
                w.Result.State = "ready";
                w.Result.Text = row.TranslatedText;
                w.Result.SourceLanguage = row.SourceLanguage;
                ListingTranslationTelemetry.Outcome("reused", target);
                return;
            }
            if (row.Status == ListingTranslationStatus.SameLanguage)
            {
                w.Result.State = "same_language";
                w.Result.SourceLanguage = row.SourceLanguage;
                return;
            }
            if (row.Status == ListingTranslationStatus.Failed && row.RetryAfter > now)
            {
                Unavailable(w.Result, row.LastError == "quota" ? "quota" : "cooldown", target, "cooldown");
                return;
            }
        }

        // Another request owns it right now (or the text just changed): ask again shortly.
        w.Result.State = "pending";
        ListingTranslationTelemetry.Outcome("pending_elsewhere", target);
    }

    private static void Unavailable(ListingTranslationFieldDto result, string reason, string target, string? outcome = null)
    {
        result.State = "unavailable";
        result.Reason = reason;
        ListingTranslationTelemetry.Outcome(outcome ?? reason, target);
    }

    private static void Summarise(ListingTranslationDto dto)
    {
        var fields = new[] { dto.Title, dto.Description }.Where(f => f.State != "empty").ToList();

        if (fields.Count == 0 || fields.All(f => f.State == "same_language"))
        {
            dto.Status = "same_language";
            return;
        }

        if (fields.Any(f => f.State == "pending"))
        {
            dto.Status = "pending";
            dto.RetryAfterSeconds = 2;
            return;
        }

        if (fields.Any(f => f.State == "ready"))
        {
            dto.Status = "ready";
            return;
        }

        dto.Status = "unavailable";
        dto.Reason = fields.Select(f => f.Reason).FirstOrDefault(r => r is not null);
    }
}
