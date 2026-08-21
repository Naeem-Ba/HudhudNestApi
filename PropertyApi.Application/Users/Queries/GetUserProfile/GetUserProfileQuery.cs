using MediatR;
using PropertyApi.Application.Users.DTOs;

namespace PropertyApi.Application.Users.Queries.GetUserProfile;

public sealed record GetUserProfileQuery(Guid UserId) : IRequest<UserProfileDto?>;
