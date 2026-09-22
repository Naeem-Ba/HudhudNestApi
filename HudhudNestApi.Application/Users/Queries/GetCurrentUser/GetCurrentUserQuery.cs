using MediatR;
using HudhudNestApi.Application.Users.DTOs;

namespace HudhudNestApi.Application.Users.Queries.GetCurrentUser;

public sealed record GetCurrentUserQuery(Guid UserId) : IRequest<UserDto?>;

