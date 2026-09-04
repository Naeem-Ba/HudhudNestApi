using MediatR;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.Commands.AddInvestmentDocument;

public sealed record AddInvestmentDocumentCommand(
    Guid InvestmentProjectId,
    InvestmentDocumentType DocumentType,
    InvestmentDocumentUploadFileDto File,
    bool IsPublic) : IRequest<Guid>;
