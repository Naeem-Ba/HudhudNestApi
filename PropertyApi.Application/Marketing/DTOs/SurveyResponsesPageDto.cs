namespace PropertyApi.Application.Marketing.DTOs;

public sealed record SurveyResponsesPageDto(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<SurveyResponseDto> Data);
