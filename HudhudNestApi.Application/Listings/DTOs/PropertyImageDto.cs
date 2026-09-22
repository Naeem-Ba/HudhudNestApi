namespace HudhudNestApi.Application.Listings.DTOs;

public sealed record PropertyImageDto(
    Guid Id,
    string Url,
    bool IsMain,
    int SortOrder);

