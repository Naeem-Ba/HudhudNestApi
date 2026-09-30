using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Observability;

namespace HudhudNestApi.Controllers;

// Deliberately separate from ObservabilitySyntheticController: this endpoint only needs
// IConfiguration/IHostEnvironment, and must keep working even when Redis rate limiting (and
// therefore IConnectionMultiplexer) isn't registered for the current environment -- which is
// the default outside Production (see Program.cs's useRedisRateLimiting). Sharing a
// controller with an action that unconditionally injects IConnectionMultiplexer would break
// this endpoint too, since ASP.NET Core constructs the whole controller instance up front.
[ApiController]
[Route("internal/observability")]
public sealed class ObservabilityAlertTestController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public ObservabilityAlertTestController(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public sealed record AlertTestStateRequest(bool Firing);

    // Lets scripts/verify-observability.sh drive a real Prometheus alert through a full
    // firing -> resolved lifecycle without ever writing to Prometheus's rule files at runtime:
    // the rule itself (observability/prometheus/rules and observability/render/prometheus)
    // is permanent and just watches this gauge. Same Staging-only + shared-secret gate as
    // ObservabilitySyntheticController.
    [HttpPost("synthetic/alert-test-state")]
    [AllowAnonymous]
    public IActionResult SetAlertTestState([FromBody] AlertTestStateRequest request)
    {
        if (!ObservabilityTestAuthorization.IsAuthorized(Request, _configuration, _environment))
        {
            return NotFound();
        }

        HudhudNestApiTelemetry.SetSyntheticAlertTestState(request.Firing);
        return Ok(new { result = "ok", firing = request.Firing });
    }
}
