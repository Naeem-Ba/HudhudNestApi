using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Listings.Mappers;

namespace HudhudNestApi.Application.Listings.Queries.GetPropertyForManagement;

public sealed class GetPropertyForManagementQueryHandler
    : IRequestHandler<GetPropertyForManagementQuery, PropertyDto>
{
    private readonly IPropertyRepository _repository;

    public GetPropertyForManagementQueryHandler(IPropertyRepository repository)
        => _repository = repository;

    public async Task<PropertyDto> Handle(
        GetPropertyForManagementQuery request,
        CancellationToken cancellationToken)
    {
        var property = await _repository.GetByIdWithDetailsAsync(
            request.PropertyId,
            cancellationToken);

        if (property is null || property.IsDeleted)
            throw new NotFoundException("Property was not found.");

        if (!request.IsAdmin && property.OwnerId != request.RequestingUserId)
            throw new ForbiddenException("Only the property owner or an administrator can manage this property.");

        return PropertyMapper.ToDto(property);
    }
}
