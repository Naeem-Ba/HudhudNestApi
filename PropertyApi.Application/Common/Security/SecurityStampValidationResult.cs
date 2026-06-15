namespace PropertyApi.Application.Common.Security;

public sealed record SecurityStampValidationResult(
    bool IsValid,
    string? FailureMessage)
{
    public static SecurityStampValidationResult Success() => new(true, null);

    public static SecurityStampValidationResult Fail(string message) => new(false, message);
}

