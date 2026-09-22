using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Mapping;
using HudhudNestApi.Application.Common.Enums;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;

namespace HudhudNestApi.Application.Agencies.Commands.SetAgencyLogo;

/// <summary>
/// Uploads the agency logo through IMediaStorageService and updates
/// Agency.LogoUrl/LogoPublicId. Same validation (type/size/signature) and same
/// "delete the old storage object only after the new one is committed" cleanup order as
/// UploadUserAvatarCommandHandler — kept identical on purpose so a logo and an avatar
/// never behave differently for the same class of mistake.
/// </summary>
public sealed class SetAgencyLogoCommandHandler
    : IRequestHandler<SetAgencyLogoCommand, SetAgencyLogoResult>
{
    private const long MaxLogoSize = 2_000_000; // 2 MB — same limit as the user avatar upload

    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/jpg",
            "image/png"
        };

    private static readonly HashSet<string> AllowedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png"
        };

    private readonly IAgencyRepository _agencies;
    private readonly IMediaStorageService _storage;
    private readonly IMediaFolderBuilder _folderBuilder;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetAgencyLogoCommandHandler> _logger;

    public SetAgencyLogoCommandHandler(
        IAgencyRepository agencies,
        IMediaStorageService storage,
        IMediaFolderBuilder folderBuilder,
        IUnitOfWork unitOfWork,
        ILogger<SetAgencyLogoCommandHandler> logger)
    {
        _agencies = agencies;
        _storage = storage;
        _folderBuilder = folderBuilder;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<SetAgencyLogoResult> Handle(
        SetAgencyLogoCommand request,
        CancellationToken cancellationToken)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, cancellationToken);

        if (agency is null || agency.IsDeleted)
            return SetAgencyLogoResult.NotFound("Agency was not found.");

        // Holding AgencyOwner is not enough — it must be THIS agency's owner. Same check
        // every other owner-only Agency handler makes; the [Authorize(Roles=...)] gate on
        // the endpoint only proves the caller owns *an* agency, not this one.
        if (agency.OwnerUserId != request.RequestingUserId)
            return SetAgencyLogoResult.Forbidden("Only the agency owner can change the logo.");

        var validationMessage = await ValidateLogoFileAsync(request.File, cancellationToken);
        if (validationMessage is not null)
            return SetAgencyLogoResult.ValidationFailed(validationMessage);

        if (request.File.Content.CanSeek)
        {
            request.File.Content.Position = 0;
        }

        await using var content = request.File.Content;

        var folder = _folderBuilder.BuildFolder(MediaEntityType.Agency, agency.Id, MediaCategories.Logo);
        var uploadResult = await _storage.UploadImageAsync(
            content,
            request.File.FileName,
            request.File.ContentType,
            folder,
            cancellationToken);

        if (!uploadResult.Succeeded)
            return SetAgencyLogoResult.StorageFailed(uploadResult.ErrorMessage);

        var previousPublicId = agency.LogoPublicId;
        var now = DateTime.UtcNow;

        // Domain update + persistence happen only after the upload above already
        // succeeded — never delete or forget the old logo before the new one is safely
        // stored, and never touch the database on a failed upload.
        agency.SetLogo(uploadResult.Url, uploadResult.PublicId, now);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        /*
         * Deleting the old logo from storage is non-critical — the new logo is already
         * persisted above. Wrapped in try/catch so a cleanup failure never turns a
         * successful upload into an error response (same pattern as
         * UploadUserAvatarCommandHandler).
         */
        if (!string.IsNullOrWhiteSpace(previousPublicId) &&
            previousPublicId != uploadResult.PublicId)
        {
            try
            {
                await _storage.DeleteImageAsync(previousPublicId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete previous agency logo from storage. AgencyId={AgencyId}, PreviousPublicId={PreviousPublicId}",
                    agency.Id,
                    previousPublicId);
            }
        }

        _logger.LogInformation(
            "Agency logo updated. AgencyId={AgencyId}, By={RequestingUserId}",
            agency.Id,
            request.RequestingUserId);

        var members = await _agencies.GetMembersAsync(agency.Id, cancellationToken);

        return SetAgencyLogoResult.Success(AgencyMapper.ToDto(agency, members, members.Count));
    }

    private static async Task<string?> ValidateLogoFileAsync(
        SetAgencyLogoFileDto file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return "لم يتم رفع أي صورة.";

        if (file.Length > MaxLogoSize)
            return "حجم الشعار أكبر من 2 ميجابايت.";

        if (!AllowedImageTypes.Contains(file.ContentType))
            return "نوع الملف غير مدعوم — يُسمح فقط بصور PNG وJPEG.";

        if (!AllowedImageExtensions.Contains(Path.GetExtension(file.FileName)))
            return "امتداد الملف غير مدعوم.";

        if (!await HasValidImageSignatureAsync(file.Content, file.ContentType, cancellationToken))
            return "الملف ليس صورة صالحة.";

        return null;
    }

    private static async Task<bool> HasValidImageSignatureAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (!content.CanSeek)
            return false;

        var originalPosition = content.Position;
        var header = new byte[8];
        var bytesRead = await content.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        content.Position = originalPosition;

        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => bytesRead >= 3 &&
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

            _ => false
        };
    }
}
