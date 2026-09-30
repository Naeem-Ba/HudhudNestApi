using MediatR;
using HudhudNestApi.Application.Listings.DTOs;

namespace HudhudNestApi.Application.Listings.Queries.GetPropertyForManagement;

public sealed record GetPropertyForManagementQuery(
    Guid PropertyId,
    Guid RequestingUserId,
    bool IsAdmin) : IRequest<PropertyDto>;
