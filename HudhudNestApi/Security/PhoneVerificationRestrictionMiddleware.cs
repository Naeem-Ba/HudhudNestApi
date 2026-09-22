using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Security;

public sealed class PhoneVerificationRestrictionMiddleware
{
    private readonly RequestDelegate _next;
    public PhoneVerificationRestrictionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db, IConfiguration configuration)
    {
        var enforcementEnabled = configuration.GetValue<bool>("PhoneVerification:EnforcementEnabled");
        if (enforcementEnabled && IsMutation(context.Request.Method) && !IsRecoveryPath(context.Request.Path) &&
            Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var state = await db.Users.Where(x => x.Id == userId)
                .Select(x => x.PhoneVerificationState).SingleOrDefaultAsync(context.RequestAborted);
            if (state == PhoneVerificationState.Restricted)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "PHONE_REVERIFICATION_REQUIRED",
                    message = "Phone ownership must be reverified before this operation."
                }, context.RequestAborted);
                return;
            }
        }
        await _next(context);
    }

    private static bool IsMutation(string method) => method is "POST" or "PUT" or "PATCH" or "DELETE";
    private static bool IsRecoveryPath(PathString path) =>
        path.StartsWithSegments("/api/auth/phone") || path.StartsWithSegments("/api/auth/refresh") ||
        path.StartsWithSegments("/api/auth/logout");
}
