using MediatR;
using PropertyApi.Application.Users.DTOs;

namespace PropertyApi.Application.Users.Queries.GetMyConsents;

/// <summary>Every consent record for the caller, newest first, including withdrawn ones — the reviewable proof.</summary>
public sealed record GetMyConsentsQuery(Guid UserId) : IRequest<IReadOnlyList<ConsentRecordDto>>;
