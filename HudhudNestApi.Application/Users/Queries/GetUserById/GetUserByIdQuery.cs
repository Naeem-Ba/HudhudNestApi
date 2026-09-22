using MediatR;
using HudhudNestApi.Application.Users.DTOs;

namespace HudhudNestApi.Application.Users.Queries.GetUserById;

public sealed record GetUserByIdQuery(Guid UserId) : IRequest<UserSummaryDto?>;

