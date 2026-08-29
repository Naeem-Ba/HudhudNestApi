using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;

namespace PropertyApi.Domain.Services.Entities;

/// <summary>
/// A file (report, photo set, certificate...) a provider attaches to a ServiceRequest as its
/// deliverable. Uploaded through the platform's existing IMediaStorageService — this entity
/// only records the result, it does not talk to storage itself.
///
/// Named after its parent (ServiceRequest), not ServiceReview: it has no relationship to
/// ServiceReview at all — a document can exist on a request that was never reviewed, and a
/// review carries no reference to any document.
/// </summary>
public sealed class ServiceRequestDocument : BaseEntity
{
    private ServiceRequestDocument() { }

    public Guid ServiceRequestId { get; private set; }

    public string FileUrl { get; private set; } = string.Empty;

    /// <summary>Cloudinary PublicId — needed to delete the file from storage if ever removed.</summary>
    public string? FilePublicId { get; private set; }

    /// <summary>MIME type, e.g. "application/pdf" or "image/jpeg".</summary>
    public string FileType { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public Guid UploadedByUserId { get; private set; }

    public static ServiceRequestDocument Create(
        Guid serviceRequestId,
        string fileUrl,
        string? filePublicId,
        string fileType,
        string fileName,
        Guid uploadedByUserId,
        DateTime utcNow)
    {
        if (serviceRequestId == Guid.Empty)
            throw new DomainException("لا يمكن إرفاق مستند بلا طلب خدمة.");

        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new DomainException("رابط الملف مطلوب.");

        if (string.IsNullOrWhiteSpace(fileType))
            throw new DomainException("نوع الملف مطلوب.");

        if (string.IsNullOrWhiteSpace(fileName))
            throw new DomainException("اسم الملف مطلوب.");

        if (uploadedByUserId == Guid.Empty)
            throw new DomainException("لا يمكن إرفاق مستند بلا رافع محدد.");

        return new ServiceRequestDocument
        {
            ServiceRequestId = serviceRequestId,
            FileUrl = fileUrl.Trim(),
            FilePublicId = string.IsNullOrWhiteSpace(filePublicId) ? null : filePublicId.Trim(),
            FileType = fileType.Trim(),
            FileName = fileName.Trim(),
            UploadedByUserId = uploadedByUserId,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }
}
