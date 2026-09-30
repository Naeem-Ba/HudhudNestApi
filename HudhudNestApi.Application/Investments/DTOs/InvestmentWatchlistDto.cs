namespace HudhudNestApi.Application.Investments.DTOs;

public sealed record InvestmentWatchlistDto(
    Guid InvestmentProjectId,
    DateTime CreatedAt,
    InvestmentProjectListDto Project);

public enum InvestmentWatchlistMutationStatus
{
    Success,
    NotFound,
    Conflict,
}

/// <summary>Mirrors FavoriteMutationResult — same reasoning as InvestmentInterestMutationResult.</summary>
public sealed record InvestmentWatchlistMutationResult(
    InvestmentWatchlistMutationStatus Status,
    string? Message = null)
{
    public static InvestmentWatchlistMutationResult Success(string? message = null) =>
        new(InvestmentWatchlistMutationStatus.Success, message);

    public static InvestmentWatchlistMutationResult NotFound(string? message = null) =>
        new(InvestmentWatchlistMutationStatus.NotFound, message);

    public static InvestmentWatchlistMutationResult Conflict(string? message = null) =>
        new(InvestmentWatchlistMutationStatus.Conflict, message);
}
