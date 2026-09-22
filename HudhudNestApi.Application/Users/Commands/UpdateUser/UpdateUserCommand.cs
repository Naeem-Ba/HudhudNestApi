using MediatR;
using HudhudNestApi.Application.Users.DTOs;

namespace HudhudNestApi.Application.Users.Commands.UpdateUser;

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
    string? CountryCode,
    string? Bio = null,
    string? ContactInfo = null
) : IRequest<UserDto?>;

