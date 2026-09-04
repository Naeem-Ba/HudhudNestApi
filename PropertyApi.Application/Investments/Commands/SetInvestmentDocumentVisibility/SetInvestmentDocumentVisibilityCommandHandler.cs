using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.Interfaces;

namespace PropertyApi.Application.Investments.Commands.SetInvestmentDocumentVisibility;

public sealed class SetInvestmentDocumentVisibilityCommandHandler : IRequestHandler<SetInvestmentDocumentVisibilityCommand>
{
    private readonly IInvestmentDocumentRepository _documents;
    private readonly IUnitOfWork _uow;

    public SetInvestmentDocumentVisibilityCommandHandler(IInvestmentDocumentRepository documents, IUnitOfWork uow)
    {
        _documents = documents;
        _uow = uow;
    }

    public async Task Handle(SetInvestmentDocumentVisibilityCommand request, CancellationToken ct)
    {
        var document = await _documents.GetByIdAsync(request.DocumentId, ct)
            ?? throw new NotFoundException($"Document {request.DocumentId} was not found.");

        if (document.InvestmentProjectId != request.InvestmentProjectId)
            throw new NotFoundException($"Document {request.DocumentId} was not found.");

        if (request.IsPublic)
            document.Publish();
        else
            document.Unpublish();

        await _uow.SaveChangesAsync(ct);
    }
}
