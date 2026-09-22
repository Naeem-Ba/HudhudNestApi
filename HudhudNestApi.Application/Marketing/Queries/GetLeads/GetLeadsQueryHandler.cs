using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Application.Marketing.Interfaces;

namespace HudhudNestApi.Application.Marketing.Queries.GetLeads;

public sealed class GetLeadsQueryHandler
    : IRequestHandler<GetLeadsQuery, LeadsPageDto>
{
    private readonly ILeadRepository _leads;

    public GetLeadsQueryHandler(ILeadRepository leads)
        => _leads = leads;

    public Task<LeadsPageDto> Handle(GetLeadsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;

        return _leads.GetPageAsync(page, pageSize, request.Source, request.UserType, cancellationToken);
    }
}
