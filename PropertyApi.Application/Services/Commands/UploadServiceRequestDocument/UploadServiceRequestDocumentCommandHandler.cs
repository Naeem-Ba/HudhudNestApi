using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;
using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Application.Services.Commands.UploadServiceRequestDocument;

/// <summary>
/// Images only for this vertical slice — reuses IMediaStorageService.UploadImageAsync exactly
/// as UploadPropertyImagesCommandHandler does, same size/MIME/magic-byte checks. PDF support
/// (a genuine need for scanned deeds/IDs) needs a new IMediaStorageService.UploadFileAsync
/// method and is deferred rather than widening a shared Infrastructure interface for one
/// still-narrow vertical.
/// </summary>
public sealed class UploadServiceRequestDocumentCommandHandler
    : IRequestHandler<UploadServiceRequestDocumentCommand, ServiceReviewDocumentDto>
{
    private const long MaxFileSize = 5_000_000;
    private const string DocumentFolder = "service-request-documents";

    private static readonly HashSet<string> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceReviewDocumentRepository _documents;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;

    public UploadServiceRequestDocumentCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceReviewDocumentRepository documents,
        IMediaStorageService storage,
        IUnitOfWork uow)
    {
        _requests = requests;
        _providers = providers;
        _documents = documents;
        _storage = storage;
        _uow = uow;
    }

    public async Task<ServiceReviewDocumentDto> Handle(
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
            throw new PropertyApi.Application.Common.Exceptions.ValidationException(
                nameof(request.Content), "الملف فارغ.");

        if (request.Length > MaxFileSize)
            throw new PropertyApi.Application.Common.Exceptions.ValidationException(
                nameof(request.Content), "حجم الملف أكبر من 5 ميجابايت.");

        if (!AllowedTypes.Contains(request.ContentType) ||
            !AllowedExtensions.Contains(Path.GetExtension(request.FileName)))
        {
            throw new PropertyApi.Application.Common.Exceptions.ValidationException(
                nameof(request.ContentType), "نوع الملف غير مدعوم. الأنواع المسموحة: JPEG, PNG, WEBP.");
        }

        if (request.Content.CanSeek)
            request.Content.Position = 0;

        var uploadResult = await _storage.UploadImageAsync(
            request.Content, request.FileName, request.ContentType, DocumentFolder, ct);

        if (!uploadResult.Succeeded)
            throw new ConflictException(uploadResult.ErrorMessage ?? "فشل رفع الملف.");

        var document = ServiceReviewDocument.Create(
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
}
