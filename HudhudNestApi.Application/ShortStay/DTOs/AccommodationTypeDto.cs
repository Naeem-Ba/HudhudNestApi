namespace HudhudNestApi.Application.ShortStay.DTOs;

public sealed record AccommodationTypeDto(
    int Id, string Code, string NameAr, string NameEn, string Category, string? Icon, int SortOrder);
