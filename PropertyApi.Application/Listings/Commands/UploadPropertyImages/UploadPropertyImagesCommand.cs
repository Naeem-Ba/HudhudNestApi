using MediatR;
using PropertyApi.Application.Listings.DTOs;

namespace PropertyApi.Application.Listings.Commands.UploadPropertyImages;

public sealed record UploadPropertyImagesCommand(
    Guid PropertyId,
    Guid UserId,
    IReadOnlyList<UploadPropertyImageFileDto> Files) : IRequest<UploadPropertyImagesResult>;

