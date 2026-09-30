using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;

namespace HudhudNestApi.Application.Services.Queries.GetServiceRequestDocuments;

public sealed record GetServiceRequestDocumentsQuery(
    Guid ServiceRequestId,
    Guid ActorUserId,
    bool IsAdmin) : IRequest<IReadOnlyList<ServiceRequestDocumentDto>>;

public sealed class GetServiceRequestDocumentsQueryHandler
    : IRequestHandler<GetServiceRequestDocumentsQuery, IReadOnlyList<ServiceRequestDocumentDto>>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestDocumentRepository _documents;

    public GetServiceRequestDocumentsQueryHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestDocumentRepository documents)
    {
        _requests = requests;
        _providers = providers;
        _documents = documents;
    }

    public async Task<IReadOnlyList<ServiceRequestDocumentDto>> Handle(
        GetServiceRequestDocumentsQuery request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        if (!request.IsAdmin && serviceRequest.RequesterId != request.ActorUserId)
        {
            var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct);
            if (provider is null || provider.UserId != request.ActorUserId)
                throw new ForbiddenException("لا يمكنك عرض مستندات طلب خدمة لا يخصّك.");
        }

        var documents = await _documents.GetByServiceRequestIdAsync(serviceRequest.Id, ct);
        return documents
            .OrderBy(d => d.CreatedAt)
            .Select(ServiceMapper.ToDto)
            .ToList()
            .AsReadOnly();
    }
}
