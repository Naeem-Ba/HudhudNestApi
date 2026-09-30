namespace HudhudNestApi.Application.Listings.DTOs;

public enum PropertyImageMutationStatus
{
    Success,
    NotFound,
    Forbidden,
    ValidationFailed,
    StorageFailed
}

public sealed record PropertyImageMutationResult(
    PropertyImageMutationStatus Status,
    string? Message = null)
{
    public static PropertyImageMutationResult Success() => new(PropertyImageMutationStatus.Success);
    public static PropertyImageMutationResult NotFound(string? message = null) => new(PropertyImageMutationStatus.NotFound, message);
    public static PropertyImageMutationResult Forbidden(string? message = null) => new(PropertyImageMutationStatus.Forbidden, message);
    public static PropertyImageMutationResult ValidationFailed(string message) => new(PropertyImageMutationStatus.ValidationFailed, message);
    public static PropertyImageMutationResult StorageFailed(string? message) => new(PropertyImageMutationStatus.StorageFailed, message);
}

public sealed record UploadPropertyImagesResult(
    PropertyImageMutationStatus Status,
    IReadOnlyList<PropertyImageDto> Images,
    string? Message = null)
{
    public static UploadPropertyImagesResult Success(IReadOnlyList<PropertyImageDto> images) =>
        new(PropertyImageMutationStatus.Success, images);

    public static UploadPropertyImagesResult NotFound(string? message = null) =>
        new(PropertyImageMutationStatus.NotFound, Array.Empty<PropertyImageDto>(), message);

    public static UploadPropertyImagesResult Forbidden(string? message = null) =>
        new(PropertyImageMutationStatus.Forbidden, Array.Empty<PropertyImageDto>(), message);

    public static UploadPropertyImagesResult ValidationFailed(string message) =>
        new(PropertyImageMutationStatus.ValidationFailed, Array.Empty<PropertyImageDto>(), message);

    public static UploadPropertyImagesResult StorageFailed(string? message) =>
        new(PropertyImageMutationStatus.StorageFailed, Array.Empty<PropertyImageDto>(), message);
}

public sealed record PropertyImagesQueryResult(
    bool Found,
    IReadOnlyList<PropertyImageDto> Images)
{
    public static PropertyImagesQueryResult NotFound() => new(false, Array.Empty<PropertyImageDto>());
    public static PropertyImagesQueryResult Success(IReadOnlyList<PropertyImageDto> images) => new(true, images);
}

