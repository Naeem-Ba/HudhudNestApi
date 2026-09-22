using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Marketing.Interfaces;

public interface ISurveyResponseRepository
{
    void Add(SurveyResponse response);

    Task<SurveyResponsesPageDto> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<SurveyStatsDto> GetStatsAsync(CancellationToken ct = default);
}
