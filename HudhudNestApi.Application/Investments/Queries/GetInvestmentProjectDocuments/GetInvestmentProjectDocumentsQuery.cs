using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectDocuments;

public sealed record GetInvestmentProjectDocumentsQuery(Guid InvestmentProjectId)
    : IRequest<IReadOnlyList<InvestmentDocumentDto>>;
