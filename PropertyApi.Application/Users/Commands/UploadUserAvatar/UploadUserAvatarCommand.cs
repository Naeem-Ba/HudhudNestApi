using MediatR;
using PropertyApi.Application.Users.DTOs;

namespace PropertyApi.Application.Users.Commands.UploadUserAvatar;

public sealed record UploadUserAvatarCommand(
    Guid UserId,
    UploadUserAvatarFileDto File
) : IRequest<UploadUserAvatarResult>;
