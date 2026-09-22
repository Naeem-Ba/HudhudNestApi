using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Investments.Interfaces;

namespace HudhudNestApi.Application.Investments.Commands.RemoveInvestmentDocument;

public sealed class RemoveInvestmentDocumentCommandHandler : IRequestHandler<RemoveInvestmentDocumentCommand>
{
    private readonly IInvestmentDocumentRepository _documents;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<RemoveInvestmentDocumentCommandHandler> _logger;

    public RemoveInvestmentDocumentCommandHandler(
        IInvestmentDocumentRepository documents,
        IMediaStorageService storage,
        IUnitOfWork uow,
        ILogger<RemoveInvestmentDocumentCommandHandler> logger)
    {
        _documents = documents;
        _storage = storage;
        _uow = uow;
        _logger = logger;
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
        catch (Exception ex)
        {
            // The metadata row is already gone (source of truth), so we don't rethrow — but
            // unlike before, we no longer swallow this silently: without a log line there was no
            // way to ever find this orphaned Cloudinary asset again.
            _logger.LogWarning(
                ex,
                "Failed to delete storage asset for removed investment document. DocumentId={DocumentId}, StorageKey={StorageKey}",
                document.Id,
                document.StorageKey);
        }
    }
}
