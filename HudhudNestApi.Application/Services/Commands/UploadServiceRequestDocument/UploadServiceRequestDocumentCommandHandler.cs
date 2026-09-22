using MediatR;
using HudhudNestApi.Application.Common.Enums;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Application.Services.Commands.UploadServiceRequestDocument;

/// <summary>
/// Images only for this vertical slice — reuses IMediaStorageService.UploadImageAsync exactly
/// as UploadPropertyImagesCommandHandler does, same size/MIME/magic-byte checks. PDF support
/// (a genuine need for scanned deeds/IDs) needs a new IMediaStorageService.UploadFileAsync
/// method and is deferred rather than widening a shared Infrastructure interface for one
/// still-narrow vertical.
/// </summary>
public sealed class UploadServiceRequestDocumentCommandHandler
    : IRequestHandler<UploadServiceRequestDocumentCommand, ServiceRequestDocumentDto>
{
    private const long MaxFileSize = 5_000_000;

    private static readonly HashSet<string> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestDocumentRepository _documents;
    private readonly IMediaStorageService _storage;
    private readonly IMediaFolderBuilder _folderBuilder;
    private readonly IUnitOfWork _uow;

    public UploadServiceRequestDocumentCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestDocumentRepository documents,
        IMediaStorageService storage,
        IMediaFolderBuilder folderBuilder,
        IUnitOfWork uow)
    {
        _requests = requests;
        _providers = providers;
        _documents = documents;
        _storage = storage;
        _folderBuilder = folderBuilder;
        _uow = uow;
    }

    public async Task<ServiceRequestDocumentDto> Handle(
        UploadServiceRequestDocumentCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        var isRequester = serviceRequest.RequesterId == request.ActorUserId;
        var isProvider = provider.UserId == request.ActorUserId;

        if (!isRequester && !isProvider)
            throw new ForbiddenException("لا يمكنك إرفاق مستندات لطلب خدمة لا يخصّك.");

        if (request.Length == 0)
            throw new HudhudNestApi.Application.Common.Exceptions.ValidationException(
                nameof(request.Content), "الملف فارغ.");

        if (request.Length > MaxFileSize)
            throw new HudhudNestApi.Application.Common.Exceptions.ValidationException(
                nameof(request.Content), "حجم الملف أكبر من 5 ميجابايت.");

        if (!AllowedTypes.Contains(request.ContentType) ||
            !AllowedExtensions.Contains(Path.GetExtension(request.FileName)))
        {
            throw new HudhudNestApi.Application.Common.Exceptions.ValidationException(
                nameof(request.ContentType), "نوع الملف غير مدعوم. الأنواع المسموحة: JPEG, PNG, WEBP.");
        }

        if (request.Content.CanSeek)
            request.Content.Position = 0;

        if (!await HasValidImageSignatureAsync(request.Content, request.ContentType, ct))
        {
            throw new HudhudNestApi.Application.Common.Exceptions.ValidationException(
                nameof(request.Content), "الملف ليس صورة صالحة.");
        }

        var folder = _folderBuilder.BuildFolder(
            MediaEntityType.ServiceRequest, request.ServiceRequestId, MediaCategories.Documents);
        var uploadResult = await _storage.UploadImageAsync(
            request.Content, request.FileName, request.ContentType, folder, ct);

        if (!uploadResult.Succeeded)
            throw new ConflictException(uploadResult.ErrorMessage ?? "فشل رفع الملف.");

        var document = ServiceRequestDocument.Create(
            serviceRequest.Id,
            uploadResult.Url!,
            uploadResult.PublicId,
            request.ContentType,
            request.FileName,
            request.ActorUserId,
            DateTime.UtcNow);

        await _documents.AddAsync(document, ct);
        await _uow.SaveChangesAsync(ct);

        return ServiceMapper.ToDto(document);
    }

    private static async Task<bool> HasValidImageSignatureAsync(
        Stream content,
        string contentType,
        CancellationToken ct)
    {
        if (!content.CanSeek)
            return false;

        var originalPosition = content.Position;
        var header = new byte[12];
        var bytesRead = await content.ReadAsync(header.AsMemory(0, header.Length), ct);
        content.Position = originalPosition;

        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => bytesRead >= 3 &&
                header[0] == 0xFF &&
                header[1] == 0xD8 &&
                header[2] == 0xFF,

            "image/png" => bytesRead >= 8 &&
                header[0] == 0x89 &&
                header[1] == 0x50 &&
                header[2] == 0x4E &&
                header[3] == 0x47 &&
                header[4] == 0x0D &&
                header[5] == 0x0A &&
                header[6] == 0x1A &&
                header[7] == 0x0A,

            "image/webp" => bytesRead >= 12 &&
                header[0] == 0x52 &&
                header[1] == 0x49 &&
                header[2] == 0x46 &&
                header[3] == 0x46 &&
                header[8] == 0x57 &&
                header[9] == 0x45 &&
                header[10] == 0x42 &&
                header[11] == 0x50,

            _ => false
        };
    }
}
