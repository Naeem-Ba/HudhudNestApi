using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;

namespace HudhudNestApi.Application.Agencies.Commands.SetAgencyLogo;

/// <summary>
/// Owner-only upload/replace of the agency logo via IMediaStorageService (Cloudinary
/// today) — the missing Application layer around Agency.SetLogo(), which was written and
/// unit-tested on the domain but had zero production callers (B-4b). Same shape as
/// UploadUserAvatarCommand (Users): raw bytes in, IMediaStorageService out, domain entity
/// updated with the resulting Url/PublicId.
/// </summary>
public sealed record SetAgencyLogoCommand(
    Guid AgencyId,
    Guid RequestingUserId,
    SetAgencyLogoFileDto File) : IRequest<SetAgencyLogoResult>;
