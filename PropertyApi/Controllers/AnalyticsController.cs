// ═══════════════════════════════════════════════════════════════
// ✅ إصلاح M-2: استبدال [ResponseCache] بـ [OutputCache] المدعوم بـ Redis
//
// المشكلة القديمة:
//   [ResponseCache(Duration = 300)] → يحفظ في RAM للـ Server الواحد فقط.
//   عند تشغيل عدة Instances، كل Instance يُنفِّذ الـ Query المكلفة (5 tables JOIN).
//
// الإصلاح:
//   [OutputCache(PolicyName = "market-insights")] → يحفظ في Redis الموزَّع.
//   جميع الـ Instances تقرأ من نفس الـ Cache.
//
// التعليم — الفرق بين ResponseCache و OutputCache:
//
//   ResponseCache:
//     - يُرسل Cache-Control headers للمتصفح
//     - يحفظ في RAM للـ Server نفسه فقط
//     - يختفي عند إعادة تشغيل الـ Server
//     - لا يدعم Invalidation (لا يمكن مسحه برمجياً)
//     - مناسب: تطبيق بـ Instance واحد + لا يحتاج Invalidation
//
//   OutputCache:
//     - يحفظ في Redis مشترك بين كل الـ Instances
//     - يبقى حتى بعد إعادة تشغيل Server
//     - يدعم Tag-based Invalidation (مسح الـ Cache عند تغيير البيانات)
//     - مناسب: أي Production مع عدة Instances
//
//   للمشروع قابل للتوسع عالمياً: OutputCache + Redis هو الخيار الصحيح دائماً.
// ═══════════════════════════════════════════════════════════════
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Analytics.DTOs;
using PropertyApi.Application.Analytics.Queries.GetMarketInsights;
using PropertyApi.Application.Analytics.Queries.GetPropertyStats;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly ISender _mediator;
    public AnalyticsController(ISender mediator) => _mediator = mediator;

    // ── GET /api/analytics/property/{id} ─────────────────────────────────
    /// <summary>
    /// إحصائيات العقار — للمالك فقط.
    /// لا يُستخدم Cache هنا لأن البيانات شخصية وتتغير باستمرار.
    /// </summary>
    [HttpGet("property/{propertyId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(PropertyStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPropertyStats(
        Guid propertyId, CancellationToken ct)
    {
        var ownerId = GetUserId();
        var result = await _mediator.Send(
            new GetPropertyStatsQuery(propertyId, ownerId), ct);
        return Ok(result);
    }

    // ── GET /api/analytics/market ──────────────────────────────────────────
    /// <summary>
    /// إحصائيات السوق العامة — بدون تسجيل دخول.
    /// يُستخدم OutputCache لمدة 5 دقائق مع دعم Redis الموزَّع.
    /// </summary>
    [HttpGet("market")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    // ✅ إصلاح M-2: استبدال [ResponseCache] بـ [OutputCache]
    //   PolicyName = "market-insights" → معرَّفة في Program.cs:
    //     Expire(5 min) + Tag("analytics") + VaryByQuery()
    //   Tag("analytics") يُتيح إبطال الـ Cache عند تحديث البيانات هكذا:
    //     await outputCacheStore.EvictByTagAsync("analytics", ct);
    [OutputCache(PolicyName = "market-insights")]
    [ProducesResponseType(typeof(MarketInsightsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMarketInsights(
        [FromQuery] string? countryCode,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetMarketInsightsQuery(countryCode), ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException();
        return Guid.Parse(raw);
    }
}