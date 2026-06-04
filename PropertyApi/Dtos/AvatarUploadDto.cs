using Microsoft.AspNetCore.Http;

namespace PropertyApi.Dtos;

public sealed class AvatarUploadDto
{
    public IFormFile File { get; set; } = default!;
}