using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.RemoveInvestmentDocument;

public sealed record RemoveInvestmentDocumentCommand(Guid InvestmentProjectId, Guid DocumentId, Guid RemovedByUserId) : IRequest;
