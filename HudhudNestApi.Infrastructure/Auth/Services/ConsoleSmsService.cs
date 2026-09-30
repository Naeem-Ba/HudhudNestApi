using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Security;
using System.Net.Http.Json;

namespace HudhudNestApi.Infrastructure.Auth.Services;

// ══════════════════════════════════════════════════════════════
// ConsoleSmsService — للتطوير المحلي
//
// لماذا هذا الكلاس موجود؟
// ──────────────────────
// في بيئة التطوير لا نريد إرسال رسائل SMS حقيقية.
// هذا الكلاس يطبع الرمز في الـ Console بدلاً من ذلك.
// عند الإنتاج، يُستبدل تلقائياً بمزود SMS حقيقي.
// ══════════════════════════════════════════════════════════════
public sealed class ConsoleSmsService : ISmsService
{
    private readonly ILogger<ConsoleSmsService> _logger;

    public ConsoleSmsService(ILogger<ConsoleSmsService> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken ct = default)
    {
        // Dev-mode only (never registered in Staging/Production, see
        // AuthInfrastructureRegistration) — the OTP itself is printed on purpose so a
        // developer can complete the flow without a real SMS provider, but the phone
        // number is masked; nothing here needs it in full.
        _logger.LogWarning("═══════════════════════════════════════════════");
        _logger.LogWarning(
            "  [DEV MODE] OTP for {Phone}: {OTP}",
            PiiMasking.MaskPhone(phoneNumber),
            otp);
        _logger.LogWarning("═══════════════════════════════════════════════");

        return Task.FromResult(true);
    }
}

// ══════════════════════════════════════════════════════════════
// TwilioSmsService — للإنتاج
//
// كيف تُفعّله؟
// ─────────────
// 1. سجّل في Twilio.com
// 2. احصل على: AccountSid, AuthToken, FromNumber
// 3. أضف للـ appsettings أو User Secrets:
//    "Twilio:AccountSid"  : "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
//    "Twilio:AuthToken"   : "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
//    "Twilio:FromNumber"  : "+1234567890"
// 4. تأكد أن حزمة Twilio مثبتة في HudhudNestApi.Infrastructure.
// ══════════════════════════════════════════════════════════════
public sealed class TwilioSmsService : ISmsService
{
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _fromNumber;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(
        IConfiguration config,
        ILogger<TwilioSmsService> logger)
    {
        _accountSid = config["Twilio:AccountSid"]
            ?? throw new InvalidOperationException("Twilio:AccountSid is required");

        _authToken = config["Twilio:AuthToken"]
            ?? throw new InvalidOperationException("Twilio:AuthToken is required");

        _fromNumber = config["Twilio:FromNumber"]
            ?? throw new InvalidOperationException("Twilio:FromNumber is required");

        _logger = logger;
    }

    public async Task<bool> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            Twilio.TwilioClient.Init(_accountSid, _authToken);

            var message = await Twilio.Rest.Api.V2010.Account.MessageResource.CreateAsync(
                body: $"رمز التحقق الخاص بك هو: {otp}\nصالح لمدة 5 دقائق. لا تشاركه مع أحد.",
                from: new Twilio.Types.PhoneNumber(_fromNumber),
                to: new Twilio.Types.PhoneNumber(phoneNumber));

            var isFailed =
                message.Status == Twilio.Rest.Api.V2010.Account.MessageResource.StatusEnum.Failed ||
                message.Status == Twilio.Rest.Api.V2010.Account.MessageResource.StatusEnum.Undelivered;

            if (isFailed)
            {
                _logger.LogWarning(
                    "Twilio SMS failed for {Phone}. MessageSid={MessageSid}, Status={Status}",
                    PiiMasking.MaskPhone(phoneNumber),
                    message.Sid,
                    message.Status);
            }

            return !isFailed;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("SMS sending was cancelled for {Phone}.", PiiMasking.MaskPhone(phoneNumber));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {Phone}.", PiiMasking.MaskPhone(phoneNumber));
            return false;
        }
    }
}

// ══════════════════════════════════════════════════════════════
// HttpSmsService — لمزودين محليين سوريين أو عرب
//
// يمكنك استبدال Twilio بأي مزود SMS عربي بتغيير هذا الكلاس فقط.
// مزودون شائعون: Unifonic، Infobip، MSGclub، مزود محلي.
// ══════════════════════════════════════════════════════════════
public sealed class HttpSmsService : ISmsService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly string _apiKey;
    private readonly string _fromNumber;
    private readonly TimeSpan _timeout;
    private readonly ILogger<HttpSmsService> _logger;

    public HttpSmsService(
        HttpClient httpClient,
        IOptions<SmsProviderOptions> options,
        ILogger<HttpSmsService> logger)
    {
        var value = options.Value;

        _httpClient = httpClient;
        _apiUrl = value.ApiUrl;
        _apiKey = value.ApiKey;
        _fromNumber = value.FromNumber;
        // The send runs inside the send-OTP request: without its own limit a provider that never
        // answers holds the request for the HttpClient default (100 s).
        _timeout = TimeSpan.FromSeconds(Math.Clamp(value.TimeoutSeconds, 1, 60));
        _logger = logger;
    }

    public async Task<bool> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken ct = default)
    {
        var payload = new
        {
            to = phoneNumber,
            from = _fromNumber,
            message = $"رمز التحقق: {otp} — صالح 5 دقائق",
            apiKey = _apiKey
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);

        try
        {
            var response = await _httpClient.PostAsJsonAsync(_apiUrl, payload, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "HTTP SMS provider returned non-success status code {StatusCode} for {Phone}.",
                    response.StatusCode,
                    PiiMasking.MaskPhone(phoneNumber));
            }

            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                ct.IsCancellationRequested
                    ? "HTTP SMS sending was cancelled for {Phone}."
                    : "HTTP SMS provider did not answer within the timeout for {Phone}.",
                PiiMasking.MaskPhone(phoneNumber));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP SMS failed for {Phone}.", PiiMasking.MaskPhone(phoneNumber));
            return false;
        }
    }
}