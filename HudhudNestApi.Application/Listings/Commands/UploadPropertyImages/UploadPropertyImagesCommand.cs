using MediatR;
using HudhudNestApi.Application.Listings.DTOs;

namespace HudhudNestApi.Application.Listings.Commands.UploadPropertyImages;

public sealed record UploadPropertyImagesCommand(
    Guid PropertyId,
    Guid UserId,
    IReadOnlyList<UploadPropertyImageFileDto> Files) : IRequest<UploadPropertyImagesResult>;

