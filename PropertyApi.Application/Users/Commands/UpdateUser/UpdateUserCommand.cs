using MediatR;
using PropertyApi.Application.Users.DTOs;

namespace PropertyApi.Application.Users.Commands.UpdateUser;

/// <summary>
/// Updates the currently authenticated user's profile.
/// Null values mean "do not change".
/// </summary>
public sealed record UpdateUserCommand(
    Guid UserId,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? PhoneNumber,
    string? ProfileImageUrl,
    string? PreferredLanguage,
    string? PreferredCurrency,
    string? CountryCode
) : IRequest<UserDto?>;
