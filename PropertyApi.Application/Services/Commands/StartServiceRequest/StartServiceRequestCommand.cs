using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;
using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Application.Services.Commands.StartServiceRequest;

public sealed record StartServiceRequestCommand(
    Guid ServiceRequestId,
    Guid ActorUserId) : IRequest<ServiceRequestDto>;

public sealed class StartServiceRequestCommandHandler
    : IRequestHandler<StartServiceRequestCommand, ServiceRequestDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;

    public StartServiceRequestCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestStatusHistoryRepository history,
        IUnitOfWork uow)
    {
        _requests = requests;
        _providers = providers;
        _history = history;
        _uow = uow;
    }

    public async Task<ServiceRequestDto> Handle(StartServiceRequestCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        if (provider.UserId != request.ActorUserId)
            throw new ForbiddenException("لا يمكنك بدء طلب خدمة موجّه لمزوّد آخر.");

        var fromStatus = serviceRequest.Status;
        var now = DateTime.UtcNow;

        serviceRequest.Start(now);

        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                serviceRequest.Id, fromStatus, serviceRequest.Status, request.ActorUserId,
                "بدأ تنفيذ الخدمة.", now),
            ct);

        await _uow.SaveChangesAsync(ct);

        var reloaded = await _requests.GetByIdAsync(serviceRequest.Id, ct) ?? serviceRequest;
        return ServiceMapper.ToDto(reloaded);
    }
}
