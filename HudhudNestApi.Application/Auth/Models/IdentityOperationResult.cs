namespace HudhudNestApi.Application.Auth.Models;

public sealed record IdentityOperationResult(
    bool Succeeded,
    IReadOnlyList<string> Errors)
{
    public static IdentityOperationResult Success()
        => new(true, Array.Empty<string>());

    public static IdentityOperationResult Failed(IEnumerable<string> errors)
        => new(false, errors.ToArray());

    public static IdentityOperationResult Failed(params string[] errors)
        => new(false, errors);
}
