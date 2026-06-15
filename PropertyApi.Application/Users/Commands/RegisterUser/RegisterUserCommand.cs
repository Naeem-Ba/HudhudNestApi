using MediatR;
using PropertyApi.Application.Users.DTOs;

namespace PropertyApi.Application.Users.Commands.RegisterUser;

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
    string PreferredLanguage = "en",
    string PreferredCurrency = "EUR",
    string? CountryCode = "DE"
) : IRequest<UserDto>;

