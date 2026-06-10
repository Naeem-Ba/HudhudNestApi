namespace PropertyApi.Application.Common.Models;

/// <summary>
/// Storage-agnostic file content result.
/// Useful if later the API exposes a proxy/download endpoint.
/// </summary>
public sealed record MediaFileResult(
    byte[] Content,
    string ContentType,
    string FileName);
