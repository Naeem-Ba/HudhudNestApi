using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Domain.Marketing.Entities;

namespace PropertyApi.Application.Marketing.Interfaces;

public interface ISurveyResponseRepository
{
    void Add(SurveyResponse response);

    Task<SurveyResponsesPageDto> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<SurveyStatsDto> GetStatsAsync(CancellationToken ct = default);
}
