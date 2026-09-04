using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.Interfaces;

namespace PropertyApi.Application.Investments.Commands.RemoveInvestmentDocument;

public sealed class RemoveInvestmentDocumentCommandHandler : IRequestHandler<RemoveInvestmentDocumentCommand>
{
    private readonly IInvestmentDocumentRepository _documents;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;

    public RemoveInvestmentDocumentCommandHandler(
        IInvestmentDocumentRepository documents,
        IMediaStorageService storage,
        IUnitOfWork uow)
    {
        _documents = documents;
        _storage = storage;
        _uow = uow;
    }

    public async Task Handle(RemoveInvestmentDocumentCommand request, CancellationToken ct)
    {
        var document = await _documents.GetByIdAsync(request.DocumentId, ct)
            ?? throw new NotFoundException($"Document {request.DocumentId} was not found.");

        if (document.InvestmentProjectId != request.InvestmentProjectId)
            throw new NotFoundException($"Document {request.DocumentId} was not found.");

        document.Remove(request.RemovedByUserId);
        await _uow.SaveChangesAsync(ct);

        // Best-effort provider cleanup — the metadata row (now soft-deleted) is the source of
        // truth for what is/was attached, so a storage-side failure here must not roll back the
        // removal (same non-blocking pattern as notification sends elsewhere in this codebase).
        try
        {
            await _storage.DeleteImageAsync(document.StorageKey, ct);
        }
        catch
        {
            // Swallowed deliberately — see comment above.
        }
    }
}
