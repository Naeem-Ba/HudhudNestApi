using System.Security.Cryptography;
using MediatR;
using PropertyApi.Application.Common.Enums;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Models;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Commands.AddInvestmentDocument;

/// <summary>
/// Binary content is stored by the existing media provider (IMediaStorageService / Cloudinary),
/// never in PostgreSQL — only metadata is persisted here (Phase 1 spec §9).
/// </summary>
public sealed class AddInvestmentDocumentCommandHandler : IRequestHandler<AddInvestmentDocumentCommand, Guid>
{
    private const long MaxDocumentSize = 15_000_000;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/jpeg", "image/png", "image/webp",
    };

    private readonly IInvestmentProjectRepository _projects;
    private readonly IInvestmentDocumentRepository _documents;
    private readonly IMediaStorageService _storage;
    private readonly IMediaFolderBuilder _folderBuilder;
    private readonly IUnitOfWork _uow;

    public AddInvestmentDocumentCommandHandler(
        IInvestmentProjectRepository projects,
        IInvestmentDocumentRepository documents,
        IMediaStorageService storage,
        IMediaFolderBuilder folderBuilder,
        IUnitOfWork uow)
    {
        _projects = projects;
        _documents = documents;
        _storage = storage;
        _folderBuilder = folderBuilder;
        _uow = uow;
    }

    public async Task<Guid> Handle(AddInvestmentDocumentCommand request, CancellationToken ct)
    {
        var projectExists = await _projects.GetByIdAsync(request.InvestmentProjectId, ct) is not null;
        if (!projectExists)
            throw new NotFoundException($"Investment project {request.InvestmentProjectId} was not found.");

        var file = request.File;

        if (file.Length <= 0 || file.Length > MaxDocumentSize)
            throw new ValidationException(nameof(file.Length), "حجم الملف غير صالح (الحد الأقصى 15 ميجابايت).");

        if (!AllowedContentTypes.Contains(file.ContentType))
            throw new ValidationException(nameof(file.ContentType), "نوع الملف غير مدعوم.");

        // Buffer fully so the same bytes can be hashed and then uploaded — the incoming stream
        // (e.g. an ASP.NET IFormFile stream) is not guaranteed re-readable after either step.
        await using var buffer = new MemoryStream();
        await using (file.Content)
        {
            await file.Content.CopyToAsync(buffer, ct);
        }

        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        using var uploadStream = new MemoryStream(bytes);
        var folder = _folderBuilder.BuildFolder(
            MediaEntityType.Investment, request.InvestmentProjectId, MediaCategories.Documents);
        var uploadResult = await _storage.UploadImageAsync(
            uploadStream,
            file.FileName,
            file.ContentType,
            folder,
            ct);

        if (!uploadResult.Succeeded)
            throw new ValidationException(nameof(file), uploadResult.ErrorMessage ?? "فشل رفع الملف.");

        var document = InvestmentDocument.Create(
            request.InvestmentProjectId,
            request.DocumentType,
            file.FileName,
            "Cloudinary",
            uploadResult.PublicId!,
            uploadResult.Url!,
            hash,
            request.IsPublic);

        _documents.Add(document);
        await _uow.SaveChangesAsync(ct);

        return document.Id;
    }
}
