using MediatR;

namespace PropertyApi.Application.Investments.Commands.SetInvestmentDocumentVisibility;

/// <summary>Admin-only publish/unpublish toggle for one document's metadata row. Never touches
/// the underlying binary asset — only whether a non-admin caller may ever see this document
/// (Phase 1 spec §20; identified as a Phase 1 gap during the Phase 2 repository audit — the
/// domain already exposed <c>InvestmentDocument.Publish()</c>/<c>Unpublish()</c>, but no
/// Command/endpoint called them).</summary>
public sealed record SetInvestmentDocumentVisibilityCommand(
    Guid InvestmentProjectId,
    Guid DocumentId,
    bool IsPublic) : IRequest;
