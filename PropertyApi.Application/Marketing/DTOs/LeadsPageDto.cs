namespace PropertyApi.Application.Marketing.DTOs;

public sealed record LeadsPageDto(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<LeadDto> Data);
