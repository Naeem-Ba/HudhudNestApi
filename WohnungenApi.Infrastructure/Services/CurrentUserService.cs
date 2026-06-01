using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using WohnungenApi.Application.Common.Interfaces;

namespace WohnungenApi.Infrastructure.Services;

/// <summary>
/// Reads current user identity from IHttpContextAccessor.
/// Lives in Infrastructure — the Application layer only sees ICurrentUserService.
///
/// NEW FILE: Didn't exist. Required by handlers that need the caller's identity.
///
/// Register in DI:
///   builder.Services.AddHttpContextAccessor();
///   builder.Services.AddScoped&lt;ICurrentUserService, CurrentUserService&gt;();
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http)
        => _http = http;

    public Guid? UserId
    {
        get
        {
            var claim = _http.HttpContext?
                .User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    public string? Email =>
        _http.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    public bool IsAuthenticated =>
        _http.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public IEnumerable<string> Roles =>
        _http.HttpContext?.User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
        ?? Enumerable.Empty<string>();
}