using System.Net;
using System.Security.Claims;

namespace HudhudNestApi.Security.RateLimiting;

internal static class RedisRateLimitPartitionKeyResolver
{
    public static string Resolve(HttpContext httpContext)
    {
        var ip = ResolveIp(httpContext);

        // httpContext.User مُعبّأ من AddAuthentication لأن هذا الـ middleware
        // يعمل بعد app.UseAuthentication() وقبل app.UseAuthorization() في Program.cs.
        // لذلك يمكن قراءة هوية المستخدم هنا حتى لو كان الـ endpoint سيُرفض لاحقًا بـ [Authorize].
        var userId = httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        // Endpoints مجهولة (login/register/forgot-password): تبقى IP-only كما هي،
        // لأنه لا توجد هوية مستخدم بعد قبل نجاح المصادقة.
        return string.IsNullOrEmpty(userId)
            ? $"ip:{ip}"
            : $"user:{userId}:ip:{ip}";
    }

    private static string ResolveIp(HttpContext httpContext)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;

        if (remoteIp is null)
            return "unknown";

        if (remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();

        return remoteIp.ToString();
    }
}