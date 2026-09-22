using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetAdminInvestmentDocuments;

/// <summary>Every document regardless of IsPublic — staff-only, unlike
/// GetInvestmentProjectDocumentsQuery which only ever returns public documents on a Published
/// project (Phase 1 spec §20).</summary>
public sealed record GetAdminInvestmentDocumentsQuery(Guid InvestmentProjectId)
    : IRequest<IReadOnlyList<InvestmentDocumentDto>>;
