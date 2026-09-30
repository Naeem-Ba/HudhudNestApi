using MediatR;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectDocuments;

/// <summary>Public/authenticated access only ever sees IsPublic documents — see
/// IInvestmentDocumentRepository.GetPublicDocumentsAsync (Phase 1 spec §20).</summary>
public sealed class GetInvestmentProjectDocumentsQueryHandler
    : IRequestHandler<GetInvestmentProjectDocumentsQuery, IReadOnlyList<InvestmentDocumentDto>>
{
    private readonly IInvestmentDocumentRepository _documents;

    public GetInvestmentProjectDocumentsQueryHandler(IInvestmentDocumentRepository documents) => _documents = documents;

    public Task<IReadOnlyList<InvestmentDocumentDto>> Handle(GetInvestmentProjectDocumentsQuery request, CancellationToken ct) =>
        _documents.GetPublicDocumentsAsync(request.InvestmentProjectId, ct);
}
