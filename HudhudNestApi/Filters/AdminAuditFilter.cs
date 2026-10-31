using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Filters;

/// <summary>
/// Phase 14 (D05): one audit row for every successful state-changing request an administrator makes on the
/// controllers that have no audit call of their own (investments, social distribution, location suggestions,
/// contact inbox, lookups refresh, review and photo removal).
///
/// Records who, which action, when, the masked IP, the HTTP verb, the path and the route ids. Never the request
/// body: bodies can hold credentials (a social account's connection secret) or personal data. A request by a
/// non-admin (for example an owner removing their own photo) writes nothing, and neither does a failed one.
/// </summary>
public sealed class AdminAuditFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> ReadOnlyMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS",
    };

    private readonly IAuditLogService _audit;
    private readonly ILogger<AdminAuditFilter> _logger;

    public AdminAuditFilter(IAuditLogService audit, ILogger<AdminAuditFilter> logger)
    {
        _audit = audit;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        var http = context.HttpContext;
        if (ReadOnlyMethods.Contains(http.Request.Method)
            || executed.Exception is not null && !executed.ExceptionHandled
            || !http.User.IsInRole(RoleNames.Admin)
            || !IsSuccess(executed.Result, http.Response.StatusCode))
        {
            return;
        }

        try
        {
            var controller = context.RouteData.Values["controller"]?.ToString() ?? "Unknown";
            var action = context.RouteData.Values["action"]?.ToString() ?? "Unknown";
            var routeIds = context.RouteData.Values
                .Where(v => v.Key is not ("controller" or "action"))
                .ToDictionary(v => v.Key, v => v.Value?.ToString());

            var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.FindFirstValue("sub");
            Guid? actor = Guid.TryParse(raw, out var id) ? id : null;

            await _audit.LogAsync(
                actor,
                $"Admin.{controller}.{action}",
                http.Connection.RemoteIpAddress?.ToString(),
                oldValue: null,
                newValue: JsonSerializer.Serialize(new
                {
                    method = http.Request.Method,
                    path = http.Request.Path.Value,
                    routeIds,
                    timestamp = DateTime.UtcNow,
                }),
                http.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The action already succeeded; a failing audit write must not turn it into an error response.
            _logger.LogError(ex, "Could not write the admin audit row for {Path}.", http.Request.Path);
        }
    }

    private static bool IsSuccess(IActionResult? result, int responseStatus)
    {
        var status = result is Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult { StatusCode: { } code } ? code : responseStatus;
        return status is >= 200 and < 300;
    }
}
