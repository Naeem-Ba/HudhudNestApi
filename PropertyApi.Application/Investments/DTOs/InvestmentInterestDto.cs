using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.DTOs;

public sealed record InvestmentInterestDto(
    Guid Id,
    Guid InvestmentProjectId,
    string ProjectTitle,
    InvestmentInterestStatus Status,
    DateTime CreatedAt);

public enum InvestmentInterestMutationStatus
{
    Success,
    NotFound,
    Conflict,
}

/// <summary>Result shape for Express/Withdraw interest — mirrors FavoriteMutationResult so
/// duplicate expression of interest is a handled outcome, not an exception (Phase 1 spec §31).</summary>
public sealed record InvestmentInterestMutationResult(
    InvestmentInterestMutationStatus Status,
    string? Message = null,
    InvestmentInterestDto? Interest = null)
{
    public static InvestmentInterestMutationResult Success(InvestmentInterestDto interest, string? message = null) =>
        new(InvestmentInterestMutationStatus.Success, message, interest);

    public static InvestmentInterestMutationResult NotFound(string? message = null) =>
        new(InvestmentInterestMutationStatus.NotFound, message);

    public static InvestmentInterestMutationResult Conflict(string? message = null, InvestmentInterestDto? interest = null) =>
        new(InvestmentInterestMutationStatus.Conflict, message, interest);
}
