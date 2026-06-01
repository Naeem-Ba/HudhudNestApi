using MediatR;
using WohnungenApi.Application.Users.DTOs;

namespace WohnungenApi.Application.Users.Commands.UpdateUser;

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
    bool? IsAgent,
    string? ProfileImageUrl,
    string? PreferredLanguage,
    string? PreferredCurrency,
    string? CountryCode
) : IRequest<UserDto?>;
