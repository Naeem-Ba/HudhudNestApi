using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Auth.Services;

// ══════════════════════════════════════════════════════════════
// ConsoleSmsService — للتطوير المحلي
//
// لماذا هذا الكلاس موجود؟
// ──────────────────────
// في بيئة التطوير لا نريد إرسال رسائل SMS حقيقية.
// هذا الكلاس يطبع الرمز في الـ Console بدلاً من ذلك.
// عند الإنتاج، يُستبدل تلقائياً بـ TwilioSmsService.
// ══════════════════════════════════════════════════════════════
public sealed class ConsoleSmsService : ISmsService
{
    private readonly ILogger<ConsoleSmsService> _logger;

    public ConsoleSmsService(ILogger<ConsoleSmsService> logger)
        => _logger = logger;

    public Task<bool> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken ct = default)
    {
        // في التطوير: اطبع الرمز بوضوح في السجلات
        _logger.LogWarning(
            "═══════════════════════════════════════════════");
        _logger.LogWarning(
            "  [DEV MODE] OTP for {Phone}: {OTP}",
            phoneNumber, otp);
        _logger.LogWarning(
            "═══════════════════════════════════════════════");

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
// 4. ثبِّت: dotnet add package Twilio
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
            // تثبيت: dotnet add package Twilio --project PropertyApi.Infrastructure
            // ثم أزل التعليق عن الكود أدناه:


            Twilio.TwilioClient.Init(_accountSid, _authToken);

            var message = await Twilio.Rest.Api.V2010.Account.MessageResource.CreateAsync(
                body: $"رمز التحقق الخاص بك هو: {otp}\nصالح لمدة 5 دقائق. لا تشاركه مع أحد.",
                from: new Twilio.Types.PhoneNumber(_fromNumber),
                to: new Twilio.Types.PhoneNumber(phoneNumber));

            return message.Status != Twilio.Rest.Api.V2010.Account.MessageResource.StatusEnum.Failed;


            // مؤقتاً — ارجع true حتى تُثبَّت Twilio
            await Task.Delay(100, ct); // محاكاة التأخير
            _logger.LogInformation(
                "SMS would be sent to {Phone} (Twilio not yet installed)", phoneNumber);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {Phone}", phoneNumber);
            return false;
        }
    }
}

// ══════════════════════════════════════════════════════════════
// AbstractSmsService — لمزودين محليين سوريين أو عرب
//
// يمكنك استبدال Twilio بأي مزود SMS عربي بتغيير هذا الكلاس فقط.
// مزودون شائعون: Unifonic، Infobip، MSGclub، مزود محلي
// ══════════════════════════════════════════════════════════════
public sealed class HttpSmsService : ISmsService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly string _apiKey;
    private readonly string _fromNumber;
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
        _logger = logger;
    }

    public async Task<bool> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken ct = default)
    {
        // تعديل هذا الكود حسب API المزود الذي تختاره
        var payload = new
        {
            to = phoneNumber,
            from = _fromNumber,
            message = $"رمز التحقق: {otp} — صالح 5 دقائق",
            apiKey = _apiKey,
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(_apiUrl, payload, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP SMS failed for {Phone}", phoneNumber);
            return false;
        }
    }
}
