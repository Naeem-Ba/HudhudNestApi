namespace PropertyApi.Application.Auth.Models;

public sealed record IdentityAccountSnapshot(
    Guid IdentityId,
    Guid UserAccountId,
    string? Email,
    string? PhoneNumber,
    bool EmailConfirmed,
    bool PhoneConfirmed,
    bool HasPassword);

public sealed record CreateIdentityAccount(
    Guid UserAccountId,
    string? Email,
    string? PhoneNumber,
    string? Password);
