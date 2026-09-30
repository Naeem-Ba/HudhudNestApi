using MediatR;
using HudhudNestApi.Application.Users.DTOs;

namespace HudhudNestApi.Application.Users.Commands.UploadUserAvatar;

public sealed record UploadUserAvatarCommand(
    Guid UserId,
    UploadUserAvatarFileDto File
) : IRequest<UploadUserAvatarResult>;
