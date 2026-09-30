using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Investments.DTOs;

public sealed record InvestmentUpdateDto(
    Guid Id,
    string Title,
    string Content,
    InvestmentUpdateType UpdateType,
    DateTime PublishedAt);
