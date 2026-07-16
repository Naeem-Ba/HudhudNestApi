namespace PropertyApi.Security;

public sealed class LegacyPhoneOtpDeprecationMiddleware
{
    private readonly RequestDelegate _next;
    public LegacyPhoneOtpDeprecationMiddleware(RequestDelegate next) => _next = next;
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals("/api/auth/phone/send-otp") ||
            context.Request.Path.Equals("/api/auth/phone/verify"))
        {
            context.Response.StatusCode = StatusCodes.Status410Gone;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "PHONE_OTP_FLOW_DEPRECATED",
                message = "Use the phone registration or password login endpoints."
            }, context.RequestAborted);
            return;
        }
        await _next(context);
    }
}
