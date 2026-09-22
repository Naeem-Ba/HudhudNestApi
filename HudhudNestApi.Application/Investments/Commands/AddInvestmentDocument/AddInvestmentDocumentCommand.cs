using MediatR;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Investments.Commands.AddInvestmentDocument;

public sealed record AddInvestmentDocumentCommand(
    Guid InvestmentProjectId,
    InvestmentDocumentType DocumentType,
    InvestmentDocumentUploadFileDto File,
    bool IsPublic) : IRequest<Guid>;
