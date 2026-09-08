using MediatR;
using PropertyApi.Application.Marketing.DTOs;

namespace PropertyApi.Application.Marketing.Commands.SubmitLead;

public sealed record SubmitLeadCommand(
    string FullName,
    string Phone,
    string City,
    string UserType,
    string Source,
    string? Notes,
    string? Campaign,
    Guid? OfferId,
    string? IpAddress) : IRequest<SubmitLeadResultDto>;
