using MediatR;
using WohnungenApi.Application.Users.DTOs;

namespace WohnungenApi.Application.Users.Commands.RegisterUser;

/// <summary>
/// Creates a new Identity user account.
/// Password hashing and security stamps are handled by ASP.NET Core Identity.
/// </summary>
public sealed record RegisterUserCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? DisplayName = null,
    string? PhoneNumber = null,
    bool IsAgent = false,
    string PreferredLanguage = "en",
    string PreferredCurrency = "EUR",
    string? CountryCode = "DE"
) : IRequest<UserDto>;
