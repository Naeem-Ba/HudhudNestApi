namespace HudhudNestApi.Application.Listings.DTOs;

public sealed record UploadPropertyImageFileDto(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);

