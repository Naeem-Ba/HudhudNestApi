namespace HudhudNestApi.Application.Favorites.DTOs;

public enum FavoriteMutationStatus
{
    Success,
    NotFound,
    Conflict
}

public sealed record FavoriteMutationResult(
    FavoriteMutationStatus Status,
    string? Message = null)
{
    public static FavoriteMutationResult Success(string? message = null) => new(FavoriteMutationStatus.Success, message);
    public static FavoriteMutationResult NotFound(string? message = null) => new(FavoriteMutationStatus.NotFound, message);
    public static FavoriteMutationResult Conflict(string? message = null) => new(FavoriteMutationStatus.Conflict, message);
}

