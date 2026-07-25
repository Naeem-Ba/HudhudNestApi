using MediatR;
using PropertyApi.Application.Listings.DTOs;

namespace PropertyApi.Application.Listings.Queries.GetPropertyForManagement;

public sealed record GetPropertyForManagementQuery(
    Guid PropertyId,
    Guid RequestingUserId,
    bool IsAdmin) : IRequest<PropertyDto>;
