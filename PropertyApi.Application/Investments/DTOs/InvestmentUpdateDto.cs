using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.DTOs;

public sealed record InvestmentUpdateDto(
    Guid Id,
    string Title,
    string Content,
    InvestmentUpdateType UpdateType,
    DateTime PublishedAt);
