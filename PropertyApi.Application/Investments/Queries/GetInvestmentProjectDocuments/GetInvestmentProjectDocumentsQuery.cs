using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjectDocuments;

public sealed record GetInvestmentProjectDocumentsQuery(Guid InvestmentProjectId)
    : IRequest<IReadOnlyList<InvestmentDocumentDto>>;
