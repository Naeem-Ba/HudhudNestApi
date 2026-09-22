using MediatR;
using HudhudNestApi.Application.Users.DTOs;

namespace HudhudNestApi.Application.Users.Queries.GetMyConsents;

/// <summary>Every consent record for the caller, newest first, including withdrawn ones — the reviewable proof.</summary>
public sealed record GetMyConsentsQuery(Guid UserId) : IRequest<IReadOnlyList<ConsentRecordDto>>;
