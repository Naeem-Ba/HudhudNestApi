using MediatR;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;

namespace PropertyApi.Application.Investments.Queries.GetAdminInvestmentDocuments;

public sealed class GetAdminInvestmentDocumentsQueryHandler
    : IRequestHandler<GetAdminInvestmentDocumentsQuery, IReadOnlyList<InvestmentDocumentDto>>
{
    private readonly IInvestmentDocumentRepository _documents;

    public GetAdminInvestmentDocumentsQueryHandler(IInvestmentDocumentRepository documents) => _documents = documents;

    public Task<IReadOnlyList<InvestmentDocumentDto>> Handle(GetAdminInvestmentDocumentsQuery request, CancellationToken ct) =>
        _documents.GetAllDocumentsForAdminAsync(request.InvestmentProjectId, ct);
}
