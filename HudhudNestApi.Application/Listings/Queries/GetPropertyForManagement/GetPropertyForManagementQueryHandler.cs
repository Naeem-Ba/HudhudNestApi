using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Listings.Mappers;
using HudhudNestApi.Application.Listings.Views;

namespace HudhudNestApi.Application.Listings.Queries.GetPropertyForManagement;

public sealed class GetPropertyForManagementQueryHandler
    : IRequestHandler<GetPropertyForManagementQuery, PropertyDto>
{
    private readonly IPropertyRepository _repository;
    private readonly IListingViewRepository _views;

    public GetPropertyForManagementQueryHandler(IPropertyRepository repository, IListingViewRepository views)
    {
        _repository = repository;
        _views = views;
    }

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

        var dto = PropertyMapper.ToDto(property);
        await _views.ApplyViewCountsAsync(new[] { dto }, cancellationToken);
        return dto;
    }
}
