using System.Security.Cryptography;
using MediatR;
using HudhudNestApi.Application.Common.Enums;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Investments.Commands.AddInvestmentDocument;

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

    // Security audit 2026-10-03, F-07: the declared Content-Type is client-controlled, so each type is
    // pinned to the extensions it may carry (the same rule every other upload applies).
    private static readonly Dictionary<string, string[]> ExtensionsByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = [".pdf"],
        ["image/jpeg"] = [".jpg", ".jpeg"],
        ["image/png"] = [".png"],
        ["image/webp"] = [".webp"],
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

        if (!HasMatchingExtension(file.FileName, file.ContentType) || !HasValidSignature(bytes, file.ContentType))
            throw new ValidationException(nameof(file.ContentType), "محتوى الملف لا يطابق نوعه المعلن.");

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

    private static bool HasMatchingExtension(string fileName, string contentType) =>
        ExtensionsByContentType.TryGetValue(contentType, out var extensions) &&
        extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    private static bool HasValidSignature(byte[] bytes, string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "application/pdf" => bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/png" => bytes.Length >= 8 &&
                bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/webp" => bytes.Length >= 12 &&
                bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false,
        };
}
