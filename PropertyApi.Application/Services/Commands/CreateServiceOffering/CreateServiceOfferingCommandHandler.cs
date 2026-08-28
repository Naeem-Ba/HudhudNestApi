using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;
using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Application.Services.Commands.CreateServiceOffering;

public sealed class CreateServiceOfferingCommandHandler
    : IRequestHandler<CreateServiceOfferingCommand, ServiceOfferingDto>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceOfferingRepository _offerings;
    private readonly IUnitOfWork _uow;

    public CreateServiceOfferingCommandHandler(
        IServiceProviderRepository providers, IServiceOfferingRepository offerings, IUnitOfWork uow)
    {
        _providers = providers;
        _offerings = offerings;
        _uow = uow;
    }

    public async Task<ServiceOfferingDto> Handle(CreateServiceOfferingCommand request, CancellationToken ct)
    {
        var provider = await _providers.GetByUserIdAsync(request.ActorUserId, ct)
            ?? throw new NotFoundException("لا يوجد ملف مزوّد خدمة لهذا المستخدم.");

        var offering = ServiceOffering.Create(
            provider.Id,
            request.Category,
            request.Title,
            request.Description,
            request.BasePrice,
            request.CurrencyId,
            request.EstimatedDurationDays,
            DateTime.UtcNow);

        await _offerings.AddAsync(offering, ct);
        await _uow.SaveChangesAsync(ct);

        // Reload with the ServiceProvider navigation included, so ServiceMapper does not
        // need a second overload just to cover the "just created" case.
        var reloaded = await _offerings.GetByIdAsync(offering.Id, ct) ?? offering;
        return ServiceMapper.ToDto(reloaded);
    }
}
