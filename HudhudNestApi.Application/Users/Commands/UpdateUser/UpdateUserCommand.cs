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
    string? ContactInfo = null,
    // Null leaves the stored governorate unchanged; 0 clears it; a positive id sets it
    // (checked against the Governorates lookup by the handler).
    int? HomeGovernorateId = null
) : IRequest<UserDto?>;

