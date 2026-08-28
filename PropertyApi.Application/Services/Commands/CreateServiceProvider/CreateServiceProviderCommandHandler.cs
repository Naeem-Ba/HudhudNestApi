using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;
using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Application.Services.Commands.CreateServiceProvider;

public sealed class CreateServiceProviderCommandHandler
    : IRequestHandler<CreateServiceProviderCommand, ServiceProviderDto>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IUnitOfWork _uow;

    public CreateServiceProviderCommandHandler(IServiceProviderRepository providers, IUnitOfWork uow)
    {
        _providers = providers;
        _uow = uow;
    }

    public async Task<ServiceProviderDto> Handle(CreateServiceProviderCommand request, CancellationToken ct)
    {
        var existing = await _providers.GetByUserIdAsync(request.UserId, ct);
        if (existing is not null)
            throw new ConflictException("لهذا المستخدم ملف مزوّد خدمة بالفعل.");

        var provider = ServiceProvider.Create(
            request.UserId,
            request.AgencyId,
            request.DisplayName,
            request.Bio,
            request.ContactEmail,
            request.ContactPhone,
            DateTime.UtcNow);

        await _providers.AddAsync(provider, ct);
        await _uow.SaveChangesAsync(ct);

        return ServiceMapper.ToDto(provider, averageRating: null, reviewCount: 0);
    }
}
