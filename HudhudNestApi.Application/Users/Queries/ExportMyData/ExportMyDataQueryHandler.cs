using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;

namespace HudhudNestApi.Application.Users.Queries.ExportMyData;

/// <summary>Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md).</summary>
public sealed class ExportMyDataQueryHandler
    : IRequestHandler<ExportMyDataQuery, AccountDataExportDto?>
{
    private readonly IUserIdentityReadService _identity;
    private readonly IAccountDataExportRepository _export;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<ExportMyDataQueryHandler> _logger;

    public ExportMyDataQueryHandler(
        IUserIdentityReadService identity,
        IAccountDataExportRepository export,
        IAuditLogService auditLogs,
        ILogger<ExportMyDataQueryHandler> logger)
    {
        _identity = identity;
        _export = export;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task<AccountDataExportDto?> Handle(
        ExportMyDataQuery request,
        CancellationToken cancellationToken)
    {
        var identity = await _identity.FindByIdAsync(request.UserId, cancellationToken);

        if (identity is null || identity.IsDeleted)
        {
            return null;
        }

        var export = await _export.BuildExportAsync(request.UserId, cancellationToken);

        // Records that an export happened (who, when) -- deliberately not what was in the
        // payload, per the same "don't log the sensitive content itself" rule this codebase
        // already follows for PII (see PiiMasking.cs).
        await _auditLogs.LogAsync(
            userId: request.UserId,
            action: AuditActions.DataExportRequested,
            ipAddress: null,
            oldValue: null,
            newValue: null,
            ct: cancellationToken);

        _logger.LogInformation(
            "Data export produced for account {UserId}.",
            request.UserId);

        return export;
    }
}
