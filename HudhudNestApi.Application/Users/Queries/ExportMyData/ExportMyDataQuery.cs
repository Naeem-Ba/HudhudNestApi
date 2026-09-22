using MediatR;
using HudhudNestApi.Application.Users.DTOs;

namespace HudhudNestApi.Application.Users.Queries.ExportMyData;

/// <summary>
/// Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md). UserId always comes from the
/// authenticated principal, never from client-supplied input, mirroring every other <c>/me</c>
/// request in this codebase -- see UsersController.ExportMyData.
/// </summary>
public sealed record ExportMyDataQuery(Guid UserId) : IRequest<AccountDataExportDto?>;
