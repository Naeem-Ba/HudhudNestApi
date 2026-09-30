using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Auth.Phone;
using HudhudNestApi.Application.Common.Observability;
using HudhudNestApi.Application.Common.Security;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Infrastructure.Auth.Otp;

/// <summary>
/// The one place that knows which channels exist, where they work, and which provider serves each. The workflow
/// never branches on a channel; it asks this service.
/// </summary>
public sealed class OtpChannelService : IOtpChannelService
{
    private static readonly OtpChannel[] DisplayOrder = [OtpChannel.Sms, OtpChannel.Telegram, OtpChannel.WhatsApp];

    private readonly OtpChannelOptions _options;
    private readonly IReadOnlyList<IOtpProvider> _providers;
    private readonly ILogger<OtpChannelService> _logger;

    public OtpChannelService(IOptions<OtpChannelOptions> options, IEnumerable<IOtpProvider> providers,
        ILogger<OtpChannelService> logger)
    {
        _options = options.Value;
        _providers = providers.ToArray();
        _logger = logger;
    }

    public IReadOnlyList<OtpChannelAvailability> Describe(string? normalizedPhone)
    {
        var states = DisplayOrder
            .Select(channel => (Channel: channel, Reason: Unavailability(channel, normalizedPhone)))
            .ToArray();
        var recommended = RecommendedFor(normalizedPhone, states.Where(x => x.Reason is null).Select(x => x.Channel));

        return states
            .Select(x => new OtpChannelAvailability(x.Channel, x.Reason is null, x.Channel == recommended,
                x.Reason, _options.UnavailableCountryCodes(x.Channel)))
            .ToArray();
    }

    public bool IsAvailable(OtpChannel channel, string normalizedPhone) =>
        Unavailability(channel, normalizedPhone) is null;

    public async Task<OtpSendResult> SendAsync(OtpChannel channel, string normalizedPhone, string code,
        CancellationToken ct)
    {
        var provider = _providers.LastOrDefault(x => x.Channel == channel);
        if (provider is null || !_options.IsEnabled(channel))
        {
            _logger.LogWarning("OTP channel {Channel} is not available; no code was sent to {Phone}.",
                channel, PiiMasking.MaskPhone(normalizedPhone));
            ApplicationTelemetry.RecordOtpSend(channel.ToString(), "channel_unavailable");
            return new OtpSendResult(OtpSendOutcome.ChannelUnavailable);
        }

        _logger.LogInformation("OTP provider {Provider} selected for channel {Channel}, recipient {Phone}.",
            provider.GetType().Name, channel, PiiMasking.MaskPhone(normalizedPhone));

        OtpSendResult result;
        try
        {
            result = await provider.SendAsync(normalizedPhone, code, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Providers are written not to throw; if one does, the exception text may carry a URL or a payload, so
            // only its type is logged.
            _logger.LogError("OTP provider {Provider} threw {ErrorType} for {Phone}.",
                provider.GetType().Name, ex.GetType().Name, PiiMasking.MaskPhone(normalizedPhone));
            result = OtpSendResult.Unavailable();
        }

        var outcome = result.Outcome switch
        {
            OtpSendOutcome.Sent => "sent",
            OtpSendOutcome.RecipientUnreachable => "unreachable",
            OtpSendOutcome.ProviderUnavailable => "provider_unavailable",
            _ => "channel_unavailable"
        };
        ApplicationTelemetry.RecordOtpSend(channel.ToString(), outcome);
        if (result.Outcome == OtpSendOutcome.Sent)
            _logger.LogInformation("OTP send succeeded on {Channel} for {Phone}.", channel, PiiMasking.MaskPhone(normalizedPhone));
        else
            _logger.LogWarning("OTP send failed on {Channel} for {Phone}: {Outcome}.", channel, PiiMasking.MaskPhone(normalizedPhone), outcome);
        return result;
    }

    private string? Unavailability(OtpChannel channel, string? normalizedPhone)
    {
        if (!_options.IsEnabled(channel) || _providers.All(x => x.Channel != channel))
            return OtpChannelUnavailableReasons.ChannelDisabled;
        if (normalizedPhone is not null && MatchesPrefix(normalizedPhone, _options.UnavailableCountryCodes(channel)))
            return OtpChannelUnavailableReasons.CountryNotSupported;
        return null;
    }

    private OtpChannel? RecommendedFor(string? normalizedPhone, IEnumerable<OtpChannel> available)
    {
        var usable = available.ToHashSet();
        string? name = null;
        if (normalizedPhone is not null)
        {
            // Keys may be written with or without the plus ("+963" or "963"): environment-variable names cannot hold a "+".
            name = _options.RecommendedByCountryCode
                .Select(x => (Prefix: CallingCode(x.Key), x.Value))
                .Where(x => normalizedPhone.StartsWith(x.Prefix, StringComparison.Ordinal))
                .OrderByDescending(x => x.Prefix.Length)
                .Select(x => x.Value)
                .FirstOrDefault();
        }
        name ??= _options.DefaultRecommended;
        return Enum.TryParse<OtpChannel>(name, true, out var channel) && usable.Contains(channel) ? channel : null;
    }

    private static string CallingCode(string key)
    {
        var trimmed = key.Trim();
        return trimmed.StartsWith('+') ? trimmed : "+" + trimmed;
    }

    private static bool MatchesPrefix(string phone, IReadOnlyList<string> prefixes) =>
        prefixes.Any(prefix => phone.StartsWith(prefix, StringComparison.Ordinal));
}
