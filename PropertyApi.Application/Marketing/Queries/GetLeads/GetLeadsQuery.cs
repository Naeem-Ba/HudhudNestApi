using MediatR;
using PropertyApi.Application.Marketing.DTOs;

namespace PropertyApi.Application.Marketing.Queries.GetLeads;

public sealed record GetLeadsQuery(
    int Page,
    int PageSize,
    string? Source,
    string? UserType) : IRequest<LeadsPageDto>;
