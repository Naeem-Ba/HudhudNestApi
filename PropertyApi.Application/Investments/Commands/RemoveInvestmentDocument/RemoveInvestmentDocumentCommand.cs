using MediatR;

namespace PropertyApi.Application.Investments.Commands.RemoveInvestmentDocument;

public sealed record RemoveInvestmentDocumentCommand(Guid InvestmentProjectId, Guid DocumentId, Guid RemovedByUserId) : IRequest;
