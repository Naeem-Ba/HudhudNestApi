using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Read-only view of the audit log for administrators (phase 14, D06). There is deliberately no write, edit
/// or delete route: the log is evidence, and ageing rows out is the retention job's business alone.
/// </summary>
[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminAuditLogsController : ControllerBase
{
    private readonly IAdminAuditLogQuery _query;

    public AdminAuditLogsController(IAdminAuditLogQuery query) => _query = query;

    // GET /api/admin/audit-logs?page=1&pageSize=50&action=&userId=&from=&to=&search=
    [HttpGet]
    [ProducesResponseType(typeof(AdminAuditLogPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? action = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (action is { Length: > 100 } || search is { Length: > 100 } || search?.Contains('\0') == true)
            return BadRequest(new { message = "action and search must be at most 100 characters." });

        if (from is not null && to is not null && from > to)
            return BadRequest(new { message = "from must not be later than to." });

        var result = await _query.SearchAsync(
            new AdminAuditLogFilter(page, pageSize, action, userId, from?.ToUniversalTime(), to?.ToUniversalTime(), search), ct);

        return Ok(new { total = result.Total, result.Page, result.PageSize, data = result.Items });
    }
}
