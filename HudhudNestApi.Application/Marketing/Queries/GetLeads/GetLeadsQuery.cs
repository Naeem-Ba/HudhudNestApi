using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;

namespace HudhudNestApi.Application.Marketing.Queries.GetLeads;

public sealed record GetLeadsQuery(
    int Page,
    int PageSize,
    string? Source,
    string? UserType) : IRequest<LeadsPageDto>;
