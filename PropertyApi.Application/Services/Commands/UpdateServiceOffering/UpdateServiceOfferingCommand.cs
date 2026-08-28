using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;

namespace PropertyApi.Application.Services.Commands.UpdateServiceOffering;

public sealed record UpdateServiceOfferingCommand(
    Guid ActorUserId,
    Guid ServiceOfferingId,
    string Title,
    string? Description,
    decimal? BasePrice,
    int? CurrencyId,
    int? EstimatedDurationDays,
    bool IsActive) : IRequest<ServiceOfferingDto>;

public sealed class UpdateServiceOfferingCommandHandler
    : IRequestHandler<UpdateServiceOfferingCommand, ServiceOfferingDto>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceOfferingRepository _offerings;
    private readonly IUnitOfWork _uow;

    public UpdateServiceOfferingCommandHandler(
        IServiceProviderRepository providers, IServiceOfferingRepository offerings, IUnitOfWork uow)
    {
        _providers = providers;
        _offerings = offerings;
        _uow = uow;
    }

    public async Task<ServiceOfferingDto> Handle(UpdateServiceOfferingCommand request, CancellationToken ct)
    {
        var offering = await _offerings.GetByIdAsync(request.ServiceOfferingId, ct)
            ?? throw new NotFoundException("الخدمة غير موجودة.");

        var provider = await _providers.GetByUserIdAsync(request.ActorUserId, ct)
            ?? throw new NotFoundException("لا يوجد ملف مزوّد خدمة لهذا المستخدم.");

        if (offering.ServiceProviderId != provider.Id)
            throw new ForbiddenException("لا يمكنك تعديل خدمة مزوّد آخر.");

        offering.UpdateDetails(
            request.Title, request.Description, request.BasePrice,
            request.CurrencyId, request.EstimatedDurationDays, DateTime.UtcNow);

        if (request.IsActive) offering.Activate(DateTime.UtcNow);
        else offering.Deactivate(DateTime.UtcNow);

        await _uow.SaveChangesAsync(ct);

        var reloaded = await _offerings.GetByIdAsync(offering.Id, ct) ?? offering;
        return ServiceMapper.ToDto(reloaded);
    }
}
