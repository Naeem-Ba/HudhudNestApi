using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;

namespace PropertyApi.Application.Services.Queries.GetServiceRequestDocuments;

public sealed record GetServiceRequestDocumentsQuery(
    Guid ServiceRequestId,
    Guid ActorUserId,
    bool IsAdmin) : IRequest<IReadOnlyList<ServiceReviewDocumentDto>>;

public sealed class GetServiceRequestDocumentsQueryHandler
    : IRequestHandler<GetServiceRequestDocumentsQuery, IReadOnlyList<ServiceReviewDocumentDto>>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceReviewDocumentRepository _documents;

    public GetServiceRequestDocumentsQueryHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceReviewDocumentRepository documents)
    {
        _requests = requests;
        _providers = providers;
        _documents = documents;
    }

    public async Task<IReadOnlyList<ServiceReviewDocumentDto>> Handle(
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
